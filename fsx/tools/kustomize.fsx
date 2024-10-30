#r "paket: nuget Fake.Core.Process ~> 6.0"

namespace Arquidev.Dbt

open Fake.Core

[<RequireQualifiedAccess>]
module Kustomize =

    let version () =
        CreateProcess.fromRawCommand "kustomize" [ "version" ]
        |> CreateProcess.ensureExitCodeWithMessage "Kustomize command failed"
        |> CreateProcess.redirectOutput
        |> Proc.run
        |> fun out -> out.Result.Output.Trim()

    let ensureVersion (requiredVersion:string) =
        let requiredVersion = if requiredVersion.StartsWith("v") then requiredVersion else $"v{requiredVersion}"
        let detectedVersion = version ()

        if detectedVersion <> requiredVersion then
            failwithf $"Kustomize version %s{requiredVersion} is required. Found: %s{detectedVersion}"

    let setImage (imageExpression: string) (workingDir: string) =
        CreateProcess.fromRawCommand "kustomize" [ "edit"; "set"; "image"; imageExpression ]
        |> CreateProcess.ensureExitCodeWithMessage "Kustomize command failed"
        |> CreateProcess.withWorkingDirectory workingDir
        |> Proc.run
        |> ignore
