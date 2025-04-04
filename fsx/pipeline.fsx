#r "paket: nuget Fake.Core.Trace ~> 6.0"

namespace Arquidev.Dbt

#load env.fsx
#load git.fsx
#load types.fsx
#load "ci/github/last-success-sha.fsx"

open Fake.Core

type Mode =
    | All
    | Diff

type PipelineEnv = {
    [<Default(nameof(Diff))>]
    DBT_MODE: Mode
}

[<RequireQualifiedAccess>]
module Pipeline =
    open System.IO
    open System.Text.RegularExpressions

    /// Find the closest ancestor dir of the originPath that contains a single file matching projectPattern
    let findParentProjectPath
        (projectPattern: string)
        (patternIgnores: string list)
        (originPath: string)
        : string option =
        let rec findParentProj (p: string) =
            match
                Directory.EnumerateFiles(p, projectPattern)
                |> Seq.filter (fun path ->
                    patternIgnores
                    |> Seq.exists (fun pattern -> Regex.IsMatch(path, pattern))
                    |> not)
                |> Seq.tryExactlyOne
            with
            | None ->
                match Directory.GetParent p with
                | null -> None
                | p -> findParentProj p.FullName
            | Some proj -> Some(proj |> Path.GetFullPath)

        findParentProj originPath

    let findRequiredProjects (dirPaths: string seq) (config: Selector) =

        dirPaths
        |> Seq.choose (findParentProjectPath config.pattern config.patternIgnores)
        |> Seq.distinct
        |> Seq.collect (config.expandLeafs config)
        |> Seq.distinct
        |> Seq.filter (not << config.isIgnored)
        |> fun paths ->
            let neitherIgnoredNorRequired =
                paths |> Seq.except (paths |> Seq.filter config.isRequired)

            for path in neitherIgnoredNorRequired do
                Trace.traceImportantfn
                    $"WARNING: {path} is a leaf project not matching the inclusion criteria. The project will be ignored."

            paths
        |> Seq.filter config.isRequired
        |> Seq.map (fun p ->
            { kind = config.kind
              path = p
              dir = FileInfo(p).DirectoryName
              safeName = config.safeName p })

    let run (selectors: Selector list) =
        let env = Env.get<PipelineEnv> ()
        Trace.tracefn $"Mode: %s{env.DBT_MODE.ToString().ToLower()}"

        let dirs =
            match env.DBT_MODE with
            | Diff ->
                Trace.traceHeader "GIT CHANGE SET"

                try
                    let lastSuccessfullyBuiltSha = LastSuccessSha.getLastSuccessCommitHash ()
                    printfn $"TEST ONLY: {lastSuccessfullyBuiltSha}"
                with _ ->
                    ()

                Env.get<GitDiffEnv> () |> Git.dirsFromDiff
            | All -> Git.allDirs ()

        selectors |> Seq.collect (findRequiredProjects dirs) |> Seq.toList
