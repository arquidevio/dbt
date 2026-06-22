namespace Arquidev.Dbt

#r "paket: nuget Arquidev.Log ~> 0.3.0"

open Arquidev.Tools
open System

[<RequireQualifiedAccess>]
module Experiment =
  let private log = Log.Source "Arquidev.Dbt.Experiment"
  let run (name: string) (enabledEnvVarName: string) (experimentFunc: unit -> 'a) : 'a option =

    let isEnabled =
      [ "1"; "true"; "TRUE"; "YES"; "Y" ]
      |> List.contains (Environment.GetEnvironmentVariable enabledEnvVarName)

    try
      try
        if isEnabled then
          log.header $"EXPERIMENT: {name}"
          experimentFunc () |> Some
        else
          None
      with exn ->
        log.error "WARN: experiment failed"
        log.error "%A" exn
        None
    finally
      if isEnabled then
        log.header $"EXPERIMENT END"
