namespace Arquidev.Dbt

#r "paket: nuget Arquidev.Log ~> 0.3 prerelease"

open Arquidev.Tools

[<AutoOpen>]
module LogSource =
  let Logger = Log.Source("Arquidev.Dbt")
