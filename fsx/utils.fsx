#r "paket:
    nuget FSharp.SystemTextJson >= 1.3"

namespace Arquidev.Dbt

open System.IO

[<RequireQualifiedAccess>]
module Utils =
    let writeEnvFile (filePath: string) (lines: (string * string) list) : unit =
        File.WriteAllLines(filePath, seq { for (k, v) in lines -> $"{k}={v}" })

[<RequireQualifiedAccess>]
module Json =
    open System.Text.Json
    open System.Text.Json.Serialization
    let write value =
        JsonSerializer.Serialize(value, JsonFSharpOptions.Default().ToJsonSerializerOptions())
