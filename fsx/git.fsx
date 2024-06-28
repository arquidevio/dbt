#r "paket:
  nuget Fake.Tools.Git >= 6.0.0
  nuget Fake.Core.Environment >= 6.0.0
  nuget Fake.Core.Trace >= 6.0.0"

namespace Arquidev.Dbt

open Fake.Core
open Fake.Tools.Git
open System
open System.IO

let env = Environment.environVar
let pwd = Directory.GetCurrentDirectory()
let git = CommandHelper.runSimpleGitCommand

let uniqueDirsWithChanges () : string seq =

    let referenceRev =
        let maybeTag = Environment.environVarOrNone "MAYBE_TAG"

        match maybeTag with
        | Some currentTag ->
            Trace.logfn $"Building tag: %s{currentTag}"
            git pwd $"describe --abbrev=0 --tags {currentTag}^"
        | None ->

            match Environment.environVarOrFail "BUILD_BASE_REF" with
            | "0000000000000000000000000000000000000000" ->
                Trace.logfn "Base ref is all zeros - defaulting to HEAD^"
                "HEAD^"
            | x -> x

    Trace.logfn $"Base ref: %s{referenceRev}"

    let currentRev = env "BUILD_CURRENT_REF"

    let dirs =
        FileStatus.getChangedFiles pwd currentRev referenceRev
        |> Seq.map (snd >> FileInfo >> (fun f -> Path.GetRelativePath(pwd, f.Directory.FullName)))
        |> Seq.filter ((<>) ".")
        |> Seq.distinct

    let info =
        if dirs |> Seq.isEmpty then
            "No meaningful changes detected"
        else
            "Detected git changes in: "

    Trace.logfn $"%s{info}"

    dirs |> Seq.iter (Trace.logfn "%s")
    dirs
