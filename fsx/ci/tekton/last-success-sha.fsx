#r "paket: nuget Arquidev.Fetch ~> 2
           nuget Arquidev.Env ~> 2"

namespace Arquidev.Dbt.Tekton

open Arquidev.Tools
open System.IO

type Summary =
  { ``type``: string
    status: string option
    end_time: System.DateTimeOffset option
    annotations: Map<string, string> }

type Result =
  { name: string
    update_time: System.DateTimeOffset
    annotations: Map<string, string>
    summary: Summary }

type ResultsResponse =
  { results: Result list
    nextPageToken: string option }

type RunDiscovery =
  | HeadSha of string
  | NoneSuccessful
  | NoneFound
  | Skipped

  member x.toOption =
    match x with
    | HeadSha commit -> Some commit
    | NoneSuccessful
    | NoneFound
    | Skipped -> None

[<RequireQualifiedAccess>]
module LastSuccessSha =

  let private commitOf (r: Result) =
    r.summary.annotations |> Map.tryFind "commit"

  let private endKey (r: Result) =
    r.summary.end_time |> Option.defaultValue r.update_time

  let internal logic (results: unit -> Result list) =
    match results () with
    | [] -> NoneFound
    | results ->
      results
      |> Seq.sortByDescending endKey
      |> Seq.tryHead
      |> Option.bind (fun r -> commitOf r |> Option.map HeadSha)
      |> Option.defaultValue NoneSuccessful

  let getLastSuccessCommitHash () =
    let context = readEnv<{| TEKTON_RESULTS_HOST: string option |}> ()

    let result =
      match context.TEKTON_RESULTS_HOST with
      | None -> Skipped
      | Some _ ->
        let env =
          readEnv<
            {| TEKTON_RESULTS_HOST: string
               RESULTS_PARENT: string
               PIPELINE_NAME: string
               GIT_BRANCH: string |}
           > ()

        let readSaToken () =
          let path = "/var/run/secrets/kubernetes.io/serviceaccount/token"
          File.ReadAllText path |> _.Trim()

        let results () =
          let filter =
            $"summary.status==SUCCESS"
            + $"&&annotations['tekton.dev/pipeline']=='{env.PIPELINE_NAME}'"
            + $"&&annotations['branch']=='{env.GIT_BRANCH}'"

          let q = "filter=" + System.Uri.EscapeDataString filter

          fetch<ResultsResponse> {
            GET
              $"https://{env.TEKTON_RESULTS_HOST}/apis/results.tekton.dev/v1alpha2/parents/{env.RESULTS_PARENT}/results?{q}&order_by=summary.end_time desc&page_size=50"

            Authorization $"Bearer {readSaToken ()}"
            Accept "application/json"
          }
          |> _.results

        try
          Fetch.enableLogs ()
          logic results
        finally
          Fetch.disableLogs ()

    printfn $"Last success sha: %A{result}"
    result
