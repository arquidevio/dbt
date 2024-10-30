namespace Arquidev.Dbt

open System.IO

[<RequireQualifiedAccess>]
module Utils =
    let writeEnvFile (filePath: string) (lines: (string * string) list) : unit =
        File.WriteAllLines(filePath, seq { for (k, v) in lines -> $"{k}={v}" })
