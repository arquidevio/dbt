namespace Arquidev.Dbt

#load "../../types.fsx"
#load "../../env.fsx"
#load "../../json.fsx"

[<AutoOpen>]
module Output =

    [<RequireQualifiedAccess>]
    module Plan =

        open System.IO

        type GithubEnv =
            { [<Default("vars.env")>]
              GITHUB_OUTPUT: string }

        let internal env = Lazy<GithubEnv>(fun () -> Env.get<GithubEnv> ())

        let appendToGithubOutputJson (key: string) (value: 'a) (planOutput: PlanOutput) =

            if key.Contains "=" then
                failwithf "Key cannot contain '='"

            File.AppendAllLines(env.Value.GITHUB_OUTPUT, [ $"{key}={value |> Json.write}" ])

            planOutput

        let appendToGithubOutputJsonWith (key: string) (value: PlanOutput -> 'a) (planOutput: PlanOutput) =
            appendToGithubOutputJson key (value planOutput) planOutput
