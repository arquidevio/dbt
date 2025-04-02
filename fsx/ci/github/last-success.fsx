namespace Arquidev.Dbt

#load "../../env.fsx"
#r "paket: nuget FsHttp ~> 15"


open FsHttp

type Step = { conclusion: string option }

type Job = { steps: Step[] }

type WorkflowRun = { id: int64; head_sha: string }

[<RequireQualifiedAccess>]
module Github =

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
            |> Response.deserializeJson<{| workflow_runs: WorkflowRun[] |}>
            |> _.workflow_runs

        let workflowRunJobs (runId: int64) =
            gh { GET $"https://api.github.com/repos/{env.GITHUB_REPOSITORY}/actions/runs/{runId}/jobs" }
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
