[![publish to nuget](https://github.com/myblindy/FastAutoMapper/actions/workflows/nuget.yml/badge.svg)](https://github.com/myblindy/FastAutoMapper/actions/workflows/nuget.yml)
[![NuGet](http://img.shields.io/nuget/v/MB.FastAutoMapper.svg)](https://www.nuget.org/packages/MB.FastAutoMapper/) [![NuGet Downloads](http://img.shields.io/nuget/dt/MB.FastAutoMapper.svg)](https://www.nuget.org/packages/MB.FastAutoMapper/)

# MB.FastAutoMapper

The aim of this project is to provide compile-time support for auto-mapping by using source generation. The ubiquitous AutoMapper instead builds the mapping code at run-time.

One of the benefits of this approach, besides paying the mapping cost at compile-time instead of run-time everytime, is that any mapping errors are also caught at compile time due to the strict type-safety of C#. 

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
