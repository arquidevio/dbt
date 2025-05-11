namespace Arquidev.Dbt

#load "log.fsx"

open System

[<RequireQualifiedAccess>]
module Experiment =
    let run (name: string) (enabledEnvVarName: string) (experimentFunc: unit -> 'a) : 'a option =

        let isEnabled =
            [ "1"; "true"; "TRUE"; "YES"; "Y" ]
            |> List.contains (Environment.GetEnvironmentVariable enabledEnvVarName)

        try
            try
                if isEnabled then
                    Log.header $"EXPERIMENT: {name}"
                    experimentFunc () |> Some
                else
                    None
            with exn ->
                Log.error "WARN: experiment failed"
                Log.error "%A" exn
                None
        finally
            if isEnabled then
                Log.header $"EXPERIMENT END"
