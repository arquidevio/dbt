#r "paket:
        nuget Fake.Core.Environment ~> 6.0
        nuget Fake.Core.Trace ~> 6.0"

namespace Arquidev.Dbt

#load "git.fsx"
#load "types.fsx"

open Fake.Core

type Mode =
    | All
    | Diff

    static member Parse(value: string) =
        match value with
        | "all" -> All
        | "diff" -> Diff
        | m -> failwithf $"Mode not supported: %s{m}"

    static member FromEnv() =
        Environment.environVarOrDefault "DBT_MODE" "diff" |> Mode.Parse

[<RequireQualifiedAccess>]
module Pipeline =
    open System.IO

    /// Find the closest ancestor dir of the originPath that contains a single file matching projectPattern
    let findParentProjectPath (projectPattern: string) (originPath: string) : string option =
        let rec findParentProj (p: string) =
            match Directory.EnumerateFiles(p, projectPattern) |> Seq.tryExactlyOne with
            | None ->
                match Directory.GetParent(p) with
                | null -> None
                | p -> findParentProj p.FullName
            | Some proj -> Some(proj |> Path.GetFullPath)

        findParentProj originPath

    /// Find unique parent projects (determined by existence of a single file matching projectPattern) of the given dirs
    let uniqueParentProjectPaths (projectPattern: string) (dirs: string seq) : string seq =
        dirs |> Seq.choose (findParentProjectPath projectPattern) |> Seq.distinct

    let findRequiredProjects (dirPaths: string seq) (config: Selector) =

        dirPaths
        |> uniqueParentProjectPaths config.pattern
        |> Seq.collect config.expandLeafs
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
        let mode = Mode.FromEnv()
        Trace.tracefn $"Mode: %s{mode.ToString().ToLower()}"

        let dirs =
            match mode with
            | Diff ->
                Trace.traceHeader "GIT CHANGE SET"
                DiffSpec.FromEnv() |> Git.dirsFromDiff
            | All -> Git.allDirs ()

        selectors |> Seq.collect (findRequiredProjects dirs) |> Seq.toList
