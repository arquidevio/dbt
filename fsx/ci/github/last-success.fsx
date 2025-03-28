namespace Arquidev.Dbt

#r "paket:
        nuget FsHttp ~> 15
        nuget Fake.Core.Environment ~> 6
"

open FsHttp
open Fake.Core

type Step = { conclusion: string option }

type Job = { steps: Step[] }

type WorkflowRun = { id: int64; head_sha: string }

[<RequireQualifiedAccess>]
module Github =

    let getEnv () =
        {| GITHUB_REPOSITORY = Environment.environVarOrFail "GITHUB_REPOSITORY"
           GITHUB_TOKEN = Environment.environVarOrFail "GITHUB_TOKEN"
           GITHUB_REF_NAME = Environment.environVarOrFail "GITHUB_REF_NAME"
           GITHUB_WORKFLOW_REF = Environment.environVarOrFail "GITHUB_WORKFLOW_REF" |}

    let getLastSuccessCommitHash () =

        let env = getEnv ()

        let workflowRuns () =
            let workflowId =
                env.GITHUB_WORKFLOW_REF.Split "@" |> Seq.head |> _.Split("/") |> Seq.last

            http {
                GET
                    $"""https://api.github.com/repos/{env.GITHUB_REPOSITORY}/actions/workflows/{workflowId}/runs?status=success&branch={env.GITHUB_REF_NAME}&page=1&per_page=10"""

                Authorization $"token {env.GITHUB_TOKEN}"
                Accept "application/vnd.github+json"
                UserAgent "FsHttp"
                print_withResponseBodyExpanded
            }
            |> Request.send
            |> Response.assertOk
            |> Response.deserializeJson<{| workflow_runs: WorkflowRun[] |}>
            |> _.workflow_runs


        let workflowRunJobs (runId: int64) =
            http {
                GET $"https://api.github.com/repos/{env.GITHUB_REPOSITORY}/actions/runs/{runId}/jobs"
                Authorization $"token {env.GITHUB_TOKEN}"
                Accept "application/vnd.github+json"
                UserAgent "FsHttp"
            }
            |> Request.send
            |> Response.deserializeJson<{| jobs: Job[] |}>
            |> _.jobs

        query {
            for r in workflowRuns () do
                where (
                    query {
                        for j in workflowRunJobs r.id do
                            for s in j.steps do
                                all (s.conclusion = Some "success")
                    }
                )

                select r.head_sha
        }
        |> Seq.tryHead
