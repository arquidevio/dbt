#r "paket: nuget Expecto ~> 10"
#load "../../fsx/ci/github/last-success-sha.fsx"

open Expecto
open Arquidev.Dbt

[<Tests>]
let tests =
    testList
        "Last successful workflow run commit SHA"
        [ test "No runs of workflow X on branch Y" {
              "Invalid result"
              |> Expect.equal (LastSuccessSha.logic (fun () -> []) (fun _ -> [])) NoneFound
          }
          test "A workflow run with no jobs (should never happen)" {
              "Invalid result"
              |> Expect.equal
                  (LastSuccessSha.logic
                      (fun () ->
                          [ { id = 2
                              head_sha = "6d7f1674455a115ec37ca14804636e0a41711ccf" } ])
                      (fun _ -> []))
                  NoneSuccessful
          }
          test "A workflow run with no successful jobs" {
              "Invalid result"
              |> Expect.equal
                  (LastSuccessSha.logic
                      (fun () ->
                          [ { id = 2
                              head_sha = "6d7f1674455a115ec37ca14804636e0a41711ccf" } ])
                      (fun _ -> [ { steps = [ { conclusion = Some "failed" } ] } ]))
                  NoneSuccessful
          }
          test "A workflow run with successful jobs" {
              "Invalid result"
              |> Expect.equal
                  (LastSuccessSha.logic
                      (fun () ->
                          [ { id = 1
                              head_sha = "8b84c09f01b29dff6be5ff2c307cef8b2dd8bd6c" }
                            { id = 2
                              head_sha = "6d7f1674455a115ec37ca14804636e0a41711ccf" }
                            { id = 3
                              head_sha = "33a32577a8212d9a03ccf18a0606c3e250083d57" }
                            { id = 4
                              head_sha = "580f42e02cf8f0b7213b50231a198123a5e09e66" } ])
                      (fun id ->
                          match id with
                          | 1L -> [ { steps = [ { conclusion = Some "success" } ] } ]
                          | 2L -> [ { steps = [ { conclusion = Some "success" }; { conclusion = Some "skipped" }] } ]
                          | 3L -> [ { steps = [] } ]
                          | 4L -> [ { steps = [ { conclusion = Some "failure" }; { conclusion = Some "success" } ] } ]
                          | _ -> []))
                  (HeadSha "6d7f1674455a115ec37ca14804636e0a41711ccf")
          } ]


runTestsWithCLIArgs [] [||] tests
