#r "paket: nuget Arquidev.Fetch ~> 1.1.1
           nuget Arquidev.Env ~> 1"

namespace Arquidev.Dbt

open Arquidev
open Arquidev.Fetch

type Step = { conclusion: string option }

type Job = { steps: Step list }

type WorkflowRun = { id: int64; head_sha: string }

type WorkflowRunDiscovery =
    | HeadSha of string
    | NoneSuccessful
    | NoneFound
    | Skipped

    member x.toOption =
        match x with
        | HeadSha commit -> Some commit
        | NoneSuccessful -> None
        | NoneFound -> None
        | Skipped -> None


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
                        | steps ->
                            steps
                            |> Seq.forall (function
                                | { conclusion = Some "success" }
                                | { conclusion = Some "skipped" } -> true
                                | _ -> false))

            )
            |> Option.map (fun r -> HeadSha r.head_sha)
            |> Option.defaultValue NoneSuccessful

    let getLastSuccessCommitHash () =

        let context = Env.get<{| GITHUB_ACTIONS: bool option |}> ()

        let result =

            if context.GITHUB_ACTIONS |> Option.defaultValue false |> not then
                Skipped
            else

                let env =
                    Env.get<
                        {| GITHUB_REPOSITORY: string
                           GITHUB_TOKEN: string
                           GITHUB_REF_NAME: string
                           GITHUB_WORKFLOW_REF: string |}
                     > ()

                let withGhHeaders =
                    rq {
                        Authorization $"token {env.GITHUB_TOKEN}"
                        Accept "application/vnd.github+json"
                        UserAgent "Arquidev.Fetch"
                    }

                let workflowRuns () =
                    let workflowId =
                        env.GITHUB_WORKFLOW_REF.Split "@" |> Seq.head |> _.Split("/") |> Seq.last

                    fetch<{| workflow_runs: WorkflowRun list |}> {
                        GET
                            $"""https://api.github.com/repos/{env.GITHUB_REPOSITORY}/actions/workflows/{workflowId}/runs?status=success&branch={env.GITHUB_REF_NAME}&page=1&per_page=10"""

                        withGhHeaders
                    }
                    |> _.workflow_runs

                let workflowRunJobs (runId: int64) =
                    fetch<{| jobs: Job list |}> {
                        GET $"https://api.github.com/repos/{env.GITHUB_REPOSITORY}/actions/runs/{runId}/jobs"
                        withGhHeaders
                    }
                    |> _.jobs

                try
                    Fetch.enableLogs ()
                    let result = logic workflowRuns workflowRunJobs
                    result
                finally
                    Fetch.disableLogs ()

        printfn $"Last success sha: %A{result}"
        result
