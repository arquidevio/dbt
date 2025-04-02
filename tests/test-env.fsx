#r "paket: nuget Expecto ~> 10"
#load "../fsx/env.fsx"

open Expecto
open Arquidev.Dbt

let env =
    Env.get<
        {| OPTIONAL_EXISTS: string option
           OPTIONAL_NOT_EXISTS: string option
           REQUIRED_EXISTS: string
           REQUIRED_INT: int
           OPTIONAL_INT_EXISTS: int option |}
     > ()

"OPTIONAL_EXISTS - unexpected value"
|> Expect.equal env.OPTIONAL_EXISTS (Some "XTYh1345")

"OPTIONAL_NOT_EXISTS - unexpected value"
|> Expect.equal env.OPTIONAL_NOT_EXISTS None

"REQUIRED_EXISTS - unexpected value" |> Expect.equal env.REQUIRED_EXISTS "4t9v7"

"REQUIRE_NOT_EXISTS should fail"
|> Expect.throws (fun () -> Env.get<{| REQUIRE_NOT_EXISTS: string |}> () |> ignore)

type MyEnv = { NORMAL_RECORD: int32 }

let recordEnv = Env.get<MyEnv> ()

"NORMAL_RECORD - unexpected value" |> Expect.equal recordEnv.NORMAL_RECORD 123

let booleanEnv =
    Env.get<
        {| BOOL_TRUE: bool
           BOOL_FALSE: bool
           BOOL_TRUE_L: bool
           BOOL_FALSE_L: bool
           BOOL_1: bool
           BOOL_0: bool
           BOOL_YES: bool
           BOOL_NO: bool
           BOOL_Y: bool
           BOOL_N: bool |}
     > ()

"BOOL_TRUE - unexpected value" |> Expect.equal booleanEnv.BOOL_TRUE true
"BOOL_FALSE - unexpected value" |> Expect.equal booleanEnv.BOOL_FALSE false
"BOOL_TRUE_L - unexpected value" |> Expect.equal booleanEnv.BOOL_TRUE_L true
"BOOL_FALSE_L - unexpected value" |> Expect.equal booleanEnv.BOOL_FALSE_L false
"BOOL_1 - unexpected value" |> Expect.equal booleanEnv.BOOL_1 true
"BOOL_0 - unexpected value" |> Expect.equal booleanEnv.BOOL_0 false
"BOOL_YES - unexpected value" |> Expect.equal booleanEnv.BOOL_YES true
"BOOL_NO - unexpected value" |> Expect.equal booleanEnv.BOOL_NO false
"BOOL_Y - unexpected value" |> Expect.equal booleanEnv.BOOL_Y true
"BOOL_N - unexpected value" |> Expect.equal booleanEnv.BOOL_N false

type MyUnion =
    | This
    | Is
    | Sparta

let duEnv = Env.get<{| UNION_VALUE: MyUnion |}> ()

"UNION_VALUE" |> Expect.equal duEnv.UNION_VALUE Sparta
