using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;

namespace FastAutoMapper.Internal;

[Generator]
public class SourceGenerator : ISourceGenerator
{
    internal static readonly string GeneratedCodeAttributeText =
        $"[System.CodeDom.Compiler.GeneratedCode(\"{Assembly.GetAssembly(typeof(SourceGenerator)).GetName().Name}\", \"{Assembly.GetAssembly(typeof(SourceGenerator)).GetName().Version}\")]";

    static IEnumerable<ISymbol> GetNestedMembers(INamedTypeSymbol namedTypeSymbol)
    {
        while (namedTypeSymbol is not null)
        {
            foreach (var member in namedTypeSymbol.GetMembers())
                yield return member;

            namedTypeSymbol = namedTypeSymbol.BaseType;
        }
    }

    public void Execute(GeneratorExecutionContext context)
    {
        context.AddSource("FastAutoMapperBaseClass.cs", $$"""
            namespace FastAutoMapper;

            {{GeneratedCodeAttributeText}}
            struct FastAutoMapperConfiguration<TFrom, TTo>
            {
                public FastAutoMapperConfiguration<TFrom, TTo> ForMember<TToField>(System.Func<TTo, TToField> toSelect, System.Func<TFrom, TToField> fromSelect) => this;
            }

            {{GeneratedCodeAttributeText}}
            class FastAutoMapperBase
            {
                public FastAutoMapperConfiguration<TFrom, TTo> CreateMap<TFrom, TTo>() => new();
            }
            """);

        var sb = new StringBuilder();
        var sr = (SyntaxReceiver)context.SyntaxContextReceiver;

        foreach (var kvp in sr.DerivedMappers)
        {
            var namespaceRequired = kvp.Key.ContainingNamespace is not null;
            if (namespaceRequired)
                sb.AppendLine($"namespace {kvp.Key.ContainingNamespace} {{");

            sb.AppendLine($$"""
                {{GeneratedCodeAttributeText}}
                partial class {{kvp.Key.Name}}
                {
                """);

            var mi = kvp.Value;
            foreach (var (fromSymbol, toSymbol, memberOverrides) in mi.TypeMaps)
            {
                sb.AppendLine($@"public {toSymbol} Map({fromSymbol} src) {{ var result = new {toSymbol}();");
                foreach (var toSymbolProperty in GetNestedMembers(toSymbol).OfType<IPropertySymbol>())
                    if (GetNestedMembers(fromSymbol).FirstOrDefault(m => m.Name == toSymbolProperty.Name) is IPropertySymbol matchingFromSymbol)
                        if (memberOverrides.FirstOrDefault(w => w.ToField == toSymbolProperty.Name) is { } @override && @override is not (null, null))
                        {
                            var lambdaParameterName = @override.FromLambdaExpression.Parameter.Identifier.Text;
                            var newExpression = @override.FromLambdaExpression.ExpressionBody.ReplaceNodes(
                                @override.FromLambdaExpression.ExpressionBody.DescendantNodes().OfType<IdentifierNameSyntax>()
                                    .Where(ins => ins.Identifier.Text == lambdaParameterName),
                                (node, n2) => SyntaxFactory.IdentifierName("src"));
                            sb.AppendLine($@"result.{toSymbolProperty.Name} = {newExpression};");
                        }
                        else
                            sb.AppendLine(@$"result.{toSymbolProperty.Name} = src.{matchingFromSymbol.Name};");
                sb.AppendLine("return result; }");
            }

            sb.AppendLine("public T Map<T>(object src) {");
            foreach (var (fromSymbol, toSymbol, memberOverrides) in mi.TypeMaps)
                sb.AppendLine($"{{ if(src is {fromSymbol} val) return (T)(object)Map(val); }}");
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
            public List<(INamedTypeSymbol From, INamedTypeSymbol To, List<(string ToField, SimpleLambdaExpressionSyntax FromLambdaExpression)> MemberOverrides)> TypeMaps = new();
        }
        public readonly Dictionary<ITypeSymbol, MapperInfo> DerivedMappers = new(SymbolEqualityComparer.IncludeNullability);

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

                    List<(string FromField, SimpleLambdaExpressionSyntax FromLambdaExpression)> overrideList = new();
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
                        && parentinvocationExpressionSyntax.ArgumentList.Arguments[1].Expression is SimpleLambdaExpressionSyntax fromSimpleLambdaExpressionSyntax)
                    {
                        // rewrite the "from" lambda expression to contain full type names with namespaces
                        var replacements = fromSimpleLambdaExpressionSyntax.DescendantNodes().OfType<IdentifierNameSyntax>()
                            .Select(ins => (symbol: context.SemanticModel.GetSymbolInfo(ins).Symbol, ins))
                            .Where(w => w.symbol?.Kind == SymbolKind.NamedType && !w.symbol.ContainingNamespace.IsGlobalNamespace)
                            .ToDictionary(w => w.ins, w => SyntaxFactory.IdentifierName($"{w.symbol}"));
                        fromSimpleLambdaExpressionSyntax = fromSimpleLambdaExpressionSyntax.ReplaceNodes(
                            replacements.Keys, (node, n2) => replacements[node]);

                        overrideList.Add((toOverrideIdentifierNameSyntax.Identifier.Text, fromSimpleLambdaExpressionSyntax));

                        parent = parent.Parent?.Parent;
                    }
                }
            }
        }
    }
}
