#r "paket: nuget Fake.Core.Trace ~> 6.0"

namespace Arquidev.Dbt

open Fake.Core
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
                    Trace.traceHeader $"EXPERIMENT: {name}"
                    experimentFunc () |> Some
                else
                    None
            with exn ->
                Trace.traceError "WARN: experiment failed"
                Trace.traceException exn
                None
        finally
            if isEnabled then
                Trace.traceHeader $"EXPERIMENT END"
