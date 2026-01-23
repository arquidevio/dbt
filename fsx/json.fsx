#r "paket: nuget FSharp.SystemTextJson ~> 1.4"

namespace Arquidev.Dbt

[<RequireQualifiedAccess>]
module Json =
    open System.Text.Json
    open System.Text.Json.Serialization
    open Microsoft.FSharp.Reflection

    let private DefaultOptions =
        JsonFSharpOptions.Default().WithSkippableOptionFields().ToJsonSerializerOptions()


    let write (value: 'a) : string =
        if FSharpType.IsFunction typeof<'a> then
            failwith "Cannot serialize function values"
        else
            JsonSerializer.Serialize(value, DefaultOptions)

    let read<'a> (value: string) : 'a =
        JsonSerializer.Deserialize<'a>(value, DefaultOptions)
