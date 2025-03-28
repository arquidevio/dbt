#load "../../fsx/ci/github/last-success.fsx"

open Arquidev.Dbt
open FsHttp

Fsi.enableDebugLogs ()

match Github.getLastSuccessCommitHash () with
| Some hash -> printfn $"%s{hash}"
| None -> failwithf "Not sure what the last successful build commit is"
