namespace Arquidev.Dbt

#load "types.fsx"

#r "paket: nuget Arquidev.Log ~> 0.3.0"

open Arquidev.Tools

[<AutoOpen>]
module PlanOutput =

  [<RequireQualifiedAccess>]
  module Plan =
    let private log = Log.Source "Arquidev.Dbt.Output"

    let summary (output: PlanOutput) =
      log.header "REQUIRED PROJECTS"

      for p in output.requiredProjects do
        log.info $"> {p.projectId} {p.fullPath}"
