#r "paket: nuget FSharp.SystemTextJson ~> 1.3"

namespace Arquidev.Dbt

[<RequireQualifiedAccess>]
module Json =
    open System.Text.Json
    open System.Text.Json.Serialization

    let private DefaultOptions =
        JsonFSharpOptions
            .Default()
            .WithSkippableOptionFields()
            .ToJsonSerializerOptions()

    let write (value: 'a) : string =
        JsonSerializer.Serialize(value, DefaultOptions)

    let read<'a> (value: string) : 'a =
        JsonSerializer.Deserialize<'a>(value, DefaultOptions)
