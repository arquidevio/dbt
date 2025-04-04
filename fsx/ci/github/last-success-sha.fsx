#load "../../env.fsx"
#r "paket: nuget FsHttp ~> 15"

namespace Arquidev.Dbt

open FsHttp

type Step = { conclusion: string option }

type Job = { steps: Step list }

type WorkflowRun = { id: int64; head_sha: string }

type WorkflowRunDiscovery =
    | HeadSha of string
    | NoneSuccessful
    | NoneFound

[<RequireQualifiedAccess>]
module LastSuccessSha =

    let internal logic (runs: unit -> WorkflowRun list) (jobs: int64 -> Job list) =

        match runs () with
        | [] -> NoneFound
        | runs ->

            runs
            |> Seq.sortByDescending _.id
            |> Seq.tryFind (fun r ->

                match jobs r.id with
                | [] -> false
                | jobs ->
                    jobs
                    |> Seq.forall (fun j ->
                        match j.steps with
                        | [] -> false
                        | steps -> steps |> Seq.forall (fun s -> s.conclusion = Some "success"))

            )
            |> Option.map (fun r -> HeadSha r.head_sha)
            |> Option.defaultValue NoneSuccessful

    let getLastSuccessCommitHash () =

        let env =
            Env.get<
                {| GITHUB_REPOSITORY: string
                   GITHUB_TOKEN: string
                   GITHUB_REF_NAME: string
                   GITHUB_WORKFLOW_REF: string |}
             > ()

        let gh =
            http {
                Authorization $"token {env.GITHUB_TOKEN}"
                Accept "application/vnd.github+json"
                UserAgent "FsHttp"
            }

        let workflowRuns () =
            let workflowId =
                env.GITHUB_WORKFLOW_REF.Split "@" |> Seq.head |> _.Split("/") |> Seq.last

            gh {
                GET
                    $"""https://api.github.com/repos/{env.GITHUB_REPOSITORY}/actions/workflows/{workflowId}/runs?status=success&branch={env.GITHUB_REF_NAME}&page=1&per_page=10"""
            }
            |> Request.send
            |> Response.assertOk
            |> Response.deserializeJson<{| workflow_runs: WorkflowRun list |}>
            |> _.workflow_runs

        let workflowRunJobs (runId: int64) =
            gh { GET $"https://api.github.com/repos/{env.GITHUB_REPOSITORY}/actions/runs/{runId}/jobs" }
            |> Request.send
            |> Response.deserializeJson<{| jobs: Job list |}>
            |> _.jobs

        try
            Fsi.enableDebugLogs()
            logic workflowRuns workflowRunJobs
        finally
            Fsi.disableDebugLogs()
