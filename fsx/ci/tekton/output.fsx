namespace Arquidev.Dbt

#load "../../types.fsx"
#load "../../json.fsx"

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
      | path -> File.WriteAllText(path, value)

      planOutput

    let writeToTektonResultWith (resultName: string) (value: PlanOutput -> string) (planOutput: PlanOutput) =
      planOutput |> writeToTektonResult resultName (planOutput |> value)

    let writeToTektonResultJson (resultName: string) (value: PlanOutput -> 'a) (planOutput: PlanOutput) =
      planOutput
      |> writeToTektonResultWith resultName (fun o -> o |> value |> Json.write)
