using System.Numerics;

partial class Mapper : FastAutoMapper.FastAutoMapperBase
{
}

namespace A
{
    class From
    {
        public string Text { get; set; }
        public string Text2 { get; set; }
        public string Extra { get; set; }
        public double[] Color { get; set; }
    }
}

namespace B
{
    class To
    {
        public string Text { get; set; }
        public string Text2 { get; set; }
        public Vector3 Color { get; set; }
        public int Info { get; set; }
    }
}

namespace P
{
    using A;
    class Program
    {
        public static void Main()
        {
            var mapper = new Mapper();
            mapper.CreateMap<From, B.To>()
                .ForMember(x => x.Color, x => new Vector3((float)x.Color[0], (float)x.Color[1], (float)x.Color[2]))
                .ForMember(x => x.Info, (x, info) => (int)info!);

            var from = new From { Text = "Text", Text2 = "Text2", Extra = "Extra", Color = new[] { 1.0, 0.5, 0.2 } };
            var to = mapper.Map(from);
        }
    }
}