using System.Numerics;
using Tweey.Actors;
using Tweey.Loaders;

namespace Tweey.Loaders
{
    enum BuildingType { WorkPlace, Storage }

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
    class BuildingTemplate
    {
        public string Name { get; set; }
        public BuildingType Type { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public Vector4 Color { get; set; }
    }
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

    class BuildingTemplateIn
    {
        public string? Name { get; set; }
        public BuildingType Type { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public float[]? Color { get; set; }
    }
}

namespace Tweey.Actors
{
    class Building : BuildingTemplate
    {
    }
}

namespace Tweey.Loaders
{
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
    class ResourceIn
    {
        public string Name { get; set; }
        public double Weight { get; set; }
        public float[] Color { get; set; }
    }
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

    public class Resource
    {
        public string? Name { get; set; }
        public double Weight { get; set; }
        public Vector4 Color { get; set; }
    }
}

namespace Tweey.Support
{
    internal partial class InternalMapper : FastAutoMapper.FastAutoMapperBase { }

    internal static class GlobalMapper
    {
        public static InternalMapper Mapper { get; } = new();

        static GlobalMapper()
        {
            Mapper.CreateMap<BuildingTemplate, Building>();
            Mapper.CreateMap<BuildingTemplateIn, BuildingTemplate>()
                .ForMember(x => x.Color, src => src.Color!.Length == 3 ? new Vector4(src.Color[0], src.Color[1], src.Color[2], 1) : new(src.Color));
            Mapper.CreateMap<ResourceIn, Resource>()
                .ForMember(x => x.Color, src => src.Color!.Length == 3 ? new Vector4(src.Color[0], src.Color[1], src.Color[2], 1) : new(src.Color));
        }

        public static void Main()
        {
            var template = new BuildingTemplate();
            GlobalMapper.Mapper.Map(template);
        }
    }
}