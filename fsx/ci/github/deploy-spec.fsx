namespace Arquidev.Dbt

#load "../../types.fsx"

#r "paket: nuget Arquidev.Env ~> 1"

open Arquidev

[<AutoOpen>]
module GitHubDeploymentSpec =

    [<RequireQualifiedAccess>]
    module Plan =

        type BuildEnv =
            { [<Env.Default("invalid")>]
              GITHUB_REPOSITORY: string
              [<Env.Default("development")>]
              GITHUB_REF_NAME: string
              [<Env.Default("00000000")>]
              GITHUB_SHA: string
              [<Env.Default("1")>]
              GITHUB_RUN_ID: int64 }

            member x.GITHUB_SHA_SHORT = x.GITHUB_SHA[..6]

        let deploymentSpec (planOutput: PlanOutput) =

            let env = Env.get<BuildEnv> ()

            { source_repo = env.GITHUB_REPOSITORY
              source_branch = env.GITHUB_REF_NAME
              new_tag = env.GITHUB_SHA_SHORT
              version = env.GITHUB_RUN_ID
              change_keys = planOutput.changeKeys
              images =
                [ for p in planOutput.requiredProjects do
                      yield { name = p.projectId; digest = None } ] }
