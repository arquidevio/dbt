namespace Arquidev.Dbt

open System.IO

[<RequireQualifiedAccess>]
module Fs =

    type WorkingDir(workingDir: string) =
        let originalDir = Directory.GetCurrentDirectory()
        do Directory.SetCurrentDirectory workingDir

        interface System.IDisposable with
            member _.Dispose() : unit =
                Directory.SetCurrentDirectory originalDir
