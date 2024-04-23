#r "paket: nuget Fake.Core.Trace >= 6.0.0"

namespace Arquidev.Dbt

open Fake.Core

[<RequireQualifiedAccess>]
module Project =

    let isRequired
        (isRequiredPorperty: string)
        (isRequired: string -> bool)
        (isTest: string -> bool)
        (projectPath: string)
        : bool =

        if projectPath |> isTest then
            false
        else
            match projectPath |> isRequired with
            | true -> true
            | false ->
                Trace.traceImportantfn
                    $"\nWARNING: {projectPath} is a leaf project without {isRequiredPorperty}=true property and will be ignored."

                Trace.traceImportantfn
                    "Either mark the project as publishable or remove it if it's dead code. You can also ignore this warning."

                false
