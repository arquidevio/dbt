#r "paket: nuget FSharp.SystemTextJson ~> 1.4"

namespace Arquidev.Dbt

[<RequireQualifiedAccess>]
module Json =
  open System.Text.Json
  open System.Text.Encodings.Web
  open System.Text.Json.Serialization
  open Microsoft.FSharp.Reflection

  let private DefaultOptions =
    let options =
      JsonFSharpOptions.Default().WithSkippableOptionFields().ToJsonSerializerOptions()

    options.Encoder <- JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    options

  let private PrettyOptions =
    let options = JsonSerializerOptions DefaultOptions
    options.WriteIndented <- true
    options

  let write (value: 'a) : string =
    if FSharpType.IsFunction typeof<'a> then
      failwith "Cannot serialize function values"
    else
      JsonSerializer.Serialize(value, DefaultOptions)

  let writePretty (value: 'a) : string =
    if FSharpType.IsFunction typeof<'a> then
      failwith "Cannot serialize function values"
    else
      JsonSerializer.Serialize(value, PrettyOptions)

  let read<'a> (value: string) : 'a =
    JsonSerializer.Deserialize<'a>(value, DefaultOptions)
