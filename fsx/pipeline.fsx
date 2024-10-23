#r "paket: nuget Fake.Core.Environment = 6.1.1"

namespace Arquidev.Dbt

#load "discover.fsx"
#load "git.fsx"
#load "project.fsx"

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

    let findRequiredProjects (dirPaths: string seq) (config: Selector) =

        dirPaths
        |> Discover.uniqueParentProjectPaths config.pattern
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
              safeName = config.safeName p })

    let run (selectors: Selector list) =
        let mode = Mode.FromEnv()
        Trace.tracefn $"Mode: %A{mode.ToString().ToLower()}"

        let dirs =
            match mode with
            | Diff ->
                Trace.traceHeader "GIT CHANGE SET"
                DiffSpec.FromEnv() |> Git.dirsFromDiff
            | All -> Git.allDirs ()

        selectors |> Seq.collect (findRequiredProjects dirs) |> Seq.toList
