#r "paket: nuget Expecto ~> 10"
#load "../fsx/json.fsx"

open Expecto
open Arquidev.Dbt

[<Tests>]
let tests =
    testList
        "JSON serde"
        [ test "Serializing a function should fail" {
              "Should crash" |> Expect.throws (fun () -> Json.write (fun () -> ()) |> ignore)
          } ]

runTestsWithCLIArgs [] [||] tests
