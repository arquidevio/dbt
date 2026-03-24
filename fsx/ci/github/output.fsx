namespace Arquidev.Dbt

#load "../../types.fsx"
#load "../../json.fsx"

#r "paket: nuget Arquidev.Env ~> 2.0.1"

open Arquidev.Tools

[<AutoOpen>]
module Output =

  [<RequireQualifiedAccess>]
  module Plan =

    open System.IO

    type GithubEnv =
      { [<Env.Default("vars.env")>]
        GITHUB_OUTPUT: string }

    let internal env = Lazy<GithubEnv>(fun () -> readEnv<GithubEnv> ())

    let appendToGithubOutput (key: string) (value: string) (planOutput: PlanOutput) =

      if key.Contains "=" then
        failwithf "Key cannot contain '='"

      File.AppendAllLines(env.Value.GITHUB_OUTPUT, [ $"{key}={value}" ])

      planOutput

    let appendToGithubOutputWith (key: string) (value: PlanOutput -> string) (planOutput: PlanOutput) =
      planOutput |> appendToGithubOutput key (planOutput |> value)

    let appendToGithubOutputJson (key: string) (value: PlanOutput -> 'a) (planOutput: PlanOutput) =
      let jsonVal = planOutput |> value |> Json.write
      planOutput |> appendToGithubOutput key jsonVal
