namespace Arquidev.Dbt

#load "../../types.fsx"
#load "../../json.fsx"
#r "paket: nuget Arquidev.Log ~> 0"

open Arquidev.Tools
open System
open System.IO

[<AutoOpen>]
module Output =

  [<RequireQualifiedAccess>]
  module Plan =

    let writeToTektonResult (resultName: string) (value: string) (planOutput: PlanOutput) =
      let resultVarName = $"RESULT_{resultName}"

      match Environment.GetEnvironmentVariable resultVarName with
      | null -> failwith $"{resultVarName} is not set"
      | path ->
        File.WriteAllText(path, value)
        Log.debug $"Tekton output written to %s{path}:\n%s{value}"

      planOutput

    let writeToTektonResultWith (resultName: string) (value: PlanOutput -> string) (planOutput: PlanOutput) =
      planOutput |> writeToTektonResult resultName (planOutput |> value)

    let writeToTektonResultObject (resultName: string) (value: PlanOutput -> 'a) (planOutput: PlanOutput) =
      planOutput
      |> writeToTektonResultWith resultName (fun o -> o |> value |> Json.write)

    let writeToTektonResultArray (resultName: string) (value: PlanOutput -> string list) (planOutput: PlanOutput) =
      planOutput
      |> writeToTektonResultWith resultName (fun o -> o |> value |> Json.write)
