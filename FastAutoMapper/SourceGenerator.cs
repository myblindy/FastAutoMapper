using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Text;

namespace FastAutoMapper.Internal;

[Generator]
public class SourceGenerator : ISourceGenerator
{
    static IEnumerable<ISymbol> GetNestedMembers(INamedTypeSymbol namedTypeSymbol)
    {
        var set = new HashSet<string>();
        while (namedTypeSymbol is not null)
        {
            foreach (var member in namedTypeSymbol.GetMembers())
                if (set.Add(member.Name))
                    yield return member;

            namedTypeSymbol = namedTypeSymbol.BaseType;
        }
    }

    public void Execute(GeneratorExecutionContext context)
    {
        context.AddSource("FastAutoMapperBaseClass.cs", @"
namespace FastAutoMapper;

struct FastAutoMapperConfiguration<TFrom, TTo>
{
    public FastAutoMapperConfiguration<TFrom, TTo> ForMember<TToField>(Func<TTo, TToField> toSelect, Func<TFrom, TToField> fromSelect) => this;
    public FastAutoMapperConfiguration<TFrom, TTo> ForMember<TToField>(Func<TTo, TToField> toSelect, Func<TFrom, object, TToField> fromSelect) => this;
}

class FastAutoMapperBase
{
    public FastAutoMapperConfiguration<TFrom, TTo> CreateMap<TFrom, TTo>() => new();
}");

        var sb = new StringBuilder();
        var sr = (SyntaxReceiver)context.SyntaxContextReceiver;

        foreach (var kvp in sr.DerivedMappers)
        {
            var namespaceRequired = kvp.Key.ContainingNamespace is INamespaceSymbol namespaceSymbol && !namespaceSymbol.IsGlobalNamespace;
            if (namespaceRequired)
                sb.AppendLine($"namespace {kvp.Key.ContainingNamespace} {{");

            sb.AppendLine($@"
partial class {kvp.Key.Name}
{{");

            var mi = kvp.Value;
            foreach (var (fromSymbol, toSymbol, memberOverrides) in mi.TypeMaps)
            {
                sb.AppendLine($@"public {toSymbol} Map({fromSymbol} src, object info = null) => new () {{");
                foreach (var toSymbolProperty in GetNestedMembers(toSymbol).OfType<IPropertySymbol>().Where(ps => !ps.IsReadOnly))
                    if (memberOverrides.FirstOrDefault(w => w.ToField == toSymbolProperty.Name) is { } @override && @override is not (null, null))
                    {
                        var lambdaParameterNames =
                            @override.FromLambdaExpression is SimpleLambdaExpressionSyntax simpleLambdaExpressionSyntax ? new[] { simpleLambdaExpressionSyntax.Parameter.Identifier.Text }
                            : @override.FromLambdaExpression is ParenthesizedLambdaExpressionSyntax parenthesizedLambdaExpressionSyntax ? parenthesizedLambdaExpressionSyntax.ParameterList.Parameters.Select(p => p.Identifier.Text).ToArray()
                            : throw new InvalidOperationException();
                        var lambdaParameterMap = new Dictionary<string, string>() { [lambdaParameterNames[0]] = "src" };
                        if (lambdaParameterNames.Length > 1) lambdaParameterMap.Add(lambdaParameterNames[1], "info");
                        if (lambdaParameterNames.Length > 2) throw new InvalidOperationException();

                        var newExpression = @override.FromLambdaExpression.ExpressionBody.ReplaceNodes(
                            @override.FromLambdaExpression.ExpressionBody.DescendantNodes().OfType<IdentifierNameSyntax>()
                                .Where(ins => lambdaParameterMap.ContainsKey(ins.Identifier.Text)),
                            (node, n2) => SyntaxFactory.IdentifierName(lambdaParameterMap[node.Identifier.Text]));
                        sb.AppendLine($@"{toSymbolProperty.Name} = {newExpression},");
                    }
                    else if (GetNestedMembers(fromSymbol).FirstOrDefault(m => m.Name == toSymbolProperty.Name) is IPropertySymbol matchingFromSymbol)
                        sb.AppendLine(@$"{toSymbolProperty.Name} = src.{matchingFromSymbol.Name},");
                sb.AppendLine("};");
            }

            sb.AppendLine("public T Map<T>(object src, object info = null) {");
            foreach (var (fromSymbol, toSymbol, memberOverrides) in mi.TypeMaps)
                sb.AppendLine($"{{ if(src is {fromSymbol} val) return (T)(object)Map(val, info); }}");
            sb.AppendLine("return default; }");

            sb.AppendLine("}");

            if (namespaceRequired)
                sb.AppendLine("}");
        }

        context.AddSource("FastAutoMapperPartialClasses.cs", sb.ToString());
    }

    public void Initialize(GeneratorInitializationContext context)
    {
        //Debugger.Launch();

        context.RegisterForSyntaxNotifications(() => new SyntaxReceiver());
    }

    class SyntaxReceiver : ISyntaxContextReceiver
    {
        internal class MapperInfo
        {
            public List<(INamedTypeSymbol From, INamedTypeSymbol To, List<(string ToField, LambdaExpressionSyntax FromLambdaExpression)> MemberOverrides)> TypeMaps = new();
        }
        public readonly Dictionary<ITypeSymbol, MapperInfo> DerivedMappers = new();

        public void OnVisitSyntaxNode(GeneratorSyntaxContext context)
        {
            if (context.Node is InvocationExpressionSyntax invocationExpressionSyntax
                && invocationExpressionSyntax.Expression is MemberAccessExpressionSyntax memberAccessExpressionSyntax
                && memberAccessExpressionSyntax.Name is GenericNameSyntax genericNameSyntax
                && genericNameSyntax.Identifier.Text == "CreateMap"
                && genericNameSyntax.TypeArgumentList.Arguments.Count == 2
                && genericNameSyntax.TypeArgumentList.Arguments[0] is IdentifierNameSyntax or QualifiedNameSyntax
                && genericNameSyntax.TypeArgumentList.Arguments[1] is IdentifierNameSyntax or QualifiedNameSyntax
                && memberAccessExpressionSyntax.Expression is IdentifierNameSyntax memberAccessObjectIdentifierNameSyntax)
            {
                var symbolInfo = context.SemanticModel.GetSymbolInfo(memberAccessObjectIdentifierNameSyntax);
                var localSymbol = symbolInfo.Symbol as ILocalSymbol;
                var propertySymbol = symbolInfo.Symbol as IPropertySymbol;

                if (localSymbol?.Type.BaseType?.Name == "FastAutoMapperBase" || propertySymbol?.Type.BaseType?.Name == "FastAutoMapperBase")
                {
                    if (!DerivedMappers.TryGetValue(localSymbol?.Type ?? propertySymbol?.Type, out var mi))
                        DerivedMappers[localSymbol?.Type ?? propertySymbol?.Type] = mi = new();

                    List<(string FromField, LambdaExpressionSyntax FromLambdaExpression)> overrideList = new();
                    mi.TypeMaps.Add(((INamedTypeSymbol)context.SemanticModel.GetSymbolInfo(genericNameSyntax.TypeArgumentList.Arguments[0]).Symbol,
                        (INamedTypeSymbol)context.SemanticModel.GetSymbolInfo(genericNameSyntax.TypeArgumentList.Arguments[1]).Symbol, overrideList));

                    var parent = context.Node.Parent?.Parent;
                    while (parent is InvocationExpressionSyntax parentinvocationExpressionSyntax
                        && parentinvocationExpressionSyntax.Expression is MemberAccessExpressionSyntax parentMemberAccessExpressionSyntax
                        && parentMemberAccessExpressionSyntax.Name.Identifier.Text == "ForMember"
                        && parentinvocationExpressionSyntax.ArgumentList.Arguments.Count == 2
                        && parentinvocationExpressionSyntax.ArgumentList.Arguments[0].Expression is SimpleLambdaExpressionSyntax toOverrideSimpleLambdaExpressionSyntax
                        && toOverrideSimpleLambdaExpressionSyntax.ExpressionBody is MemberAccessExpressionSyntax toOverrideMemberAccessExpressionSyntax
                        && toOverrideMemberAccessExpressionSyntax.Name is IdentifierNameSyntax toOverrideIdentifierNameSyntax
                        && parentinvocationExpressionSyntax.ArgumentList.Arguments[1].Expression is LambdaExpressionSyntax fromLambdaExpressionSyntax)
                    {
                        // rewrite the "from" lambda expression to contain full type names with namespaces
                        var replacements = fromLambdaExpressionSyntax.DescendantNodes().OfType<IdentifierNameSyntax>()
                            .Select(ins => (symbol: context.SemanticModel.GetSymbolInfo(ins).Symbol, ins))
                            .Where(w => w.symbol?.Kind == SymbolKind.NamedType && !w.symbol.ContainingNamespace.IsGlobalNamespace)
                            .ToDictionary(w => w.ins, w => SyntaxFactory.IdentifierName($"{w.symbol}"));
                        fromLambdaExpressionSyntax = fromLambdaExpressionSyntax.ReplaceNodes(replacements.Keys, (node, n2) => replacements[node]);

                        overrideList.Add((toOverrideIdentifierNameSyntax.Identifier.Text, fromLambdaExpressionSyntax));

                        parent = parent.Parent?.Parent;
                    }
                }
            }
        }
    }
}
