#load "../../fsx/ci/github/last-success.fsx"

open Arquidev.Dbt
open FsHttp

Fsi.enableDebugLogs ()

let value =
    Github.getLastSuccessCommitHash
        "arquidevio"
        "dbt"
        "test.yaml"
        (System.Environment.GetEnvironmentVariable "GITHUB_TOKEN")
        "main"

match value with
| Some hash -> printfn $"%s{hash}"
| None -> failwithf "Not sure what the last successful build commit is"
