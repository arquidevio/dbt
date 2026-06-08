namespace Arquidev.Dbt

#load "logsource.fsx"

open Arquidev.Tools
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
          Logger.header $"EXPERIMENT: {name}"
          experimentFunc () |> Some
        else
          None
      with exn ->
        Logger.error "WARN: experiment failed"
        Logger.error "%A" exn
        None
    finally
      if isEnabled then
        Logger.header $"EXPERIMENT END"
