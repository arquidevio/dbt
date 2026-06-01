namespace Arquidev.Dbt

#load "../../types.fsx"

#r "paket: nuget Arquidev.Env ~> 2"

open Arquidev.Tools

[<AutoOpen>]
module TektonDeploymentSpec =

  [<RequireQualifiedAccess>]
  module Plan =

    // Expected env vars — set these in the PipelineRun params using PAC template variables:
    type BuildEnv =
      { [<Env.Default("invalid")>]
        DBT_SOURCE_REPO: string // ← {{ repo_url }}
        [<Env.Default("unknown")>]
        DBT_SOURCE_BRANCH: string // ← {{ source_branch }}
        [<Env.Default("00000000")>]
        DBT_REVISION: string // ← {{ revision }}
        [<Env.Default("0")>]
        DBT_RUN_VERSION: int64  // ← pipelinesascode.tekton.dev/check-run-id (Downward API annotation,
      //   not a PAC template var — inject via fieldRef in the task step)
      //   NOTE: this annotation may differ depending on source forge
      }

      member x.DBT_REVISION_SHORT = x.DBT_REVISION[..6]

    let deploymentSpec (planOutput: PlanOutput) =

      let env = readEnv<BuildEnv> ()

      { source_repo = env.DBT_SOURCE_REPO
        source_branch = env.DBT_SOURCE_BRANCH
        new_tag = env.DBT_REVISION_SHORT
        version = env.DBT_RUN_VERSION
        change_keys = planOutput.changeKeys
        images =
          [ for p in planOutput.requiredProjects do
              yield { name = p.projectId; digest = None } ] }
