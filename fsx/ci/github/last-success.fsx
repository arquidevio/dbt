namespace Arquidev.Dbt

#r "paket:
        nuget FsHttp ~> 15
"

open FsHttp


type Step = { conclusion: string option }

type Job = { steps: Step[] }

type WorkflowRun = { id: int64; head_sha: string }

[<RequireQualifiedAccess>]
module Github =

    let getLastSuccessCommitHash (owner: string) (repo: string) (workflowId: string) (token: string) (branch: string) =

        let workflowRuns () =
            http {
                GET
                    $"https://api.github.com/repos/{owner}/{repo}/actions/workflows/{workflowId}/runs?status=success&branch={branch}&page=1&per_page=10"

                Authorization $"token {token}"
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
                GET $"https://api.github.com/repos/{owner}/{repo}/actions/runs/{runId}/jobs"
                Authorization $"token {token}"
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
