#r "paket: nuget Fake.Core.Trace ~> 6.0"

namespace Arquidev.Dbt

open Fake.Core
open System

[<RequireQualifiedAccess>]
module Experiment =
    let run (name: string) (enabledEnvVarName: string) (experimentFunc: unit -> unit) =

        let isEnabled =
            [ "1"; "true"; "TRUE"; "YES"; "Y" ]
            |> List.contains (Environment.GetEnvironmentVariable enabledEnvVarName)

        try
            try
                if isEnabled then
                    Trace.traceHeader $"EXPERIMENT: {name}"
                    experimentFunc ()
            with exn ->
                Trace.traceError "WARN: experiment failed"
                Trace.traceException exn
        finally
            if isEnabled then
                Trace.traceHeader $"EXPERIMENT END"
