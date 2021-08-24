# FastAutoMapper

The aim of this project is to provide compile-time support for auto-mapping by using source generation. The ubiquitous AutoMapper instead builds the mapping code at run-time.

The code is pretty simple to use, simply reference the NuGet package `MB.FastAutoMapper` and write something like this:

```C#
using FastAutoMapper;

class Src
{
  public string Text { get; set; }
  public float[] Color { get; set; }
}

class Dst
{
  public string Text { get; set; }
  public Vector3 Color { get; set; }
}

// first declare the mapper class, make sure it is a partial type
partial class Mapper : FastAutoMapperBase { }

// then declare your instance
var mapper = new Mapper();

// define the maps you need, including any conversions with `ForMember`
mapper.CreateMap<From, To>()
  .ForMember(x => x.Color, x => new Vector3(x.Color[0], x.Color[1], x.Color[2]));
  
// and map your instances
var dst = mapper.Map(new Src { Text = "meep", Color = new[] { .1f, .5f, 1f } });
```
