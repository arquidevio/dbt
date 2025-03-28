#r "paket:
  nuget Fake.Tools.Git ~> 6.0
  nuget Fake.Core.Environment ~> 6.0
  nuget Fake.Core.Trace ~> 6.0"

namespace Arquidev.Dbt

open Fake.Core
open Fake.Tools.Git
open System.IO

type DiffSpec =
    { CurrentCommit: string option
      BaseCommit: string option
      MaybeTag: string option }

    static member FromEnv() =
        { CurrentCommit = Environment.environVarOrNone "DBT_CURRENT_COMMIT"
          BaseCommit = Environment.environVarOrNone "DBT_BASE_COMMIT"
          MaybeTag = Environment.environVarOrNone "DBT_MAYBE_TAG" }

[<RequireQualifiedAccess>]
module Git =

    let env = Environment.environVarOrDefault
    let pwd = Directory.GetCurrentDirectory()
    let git = CommandHelper.runSimpleGitCommand

    let allDirs () : string seq =
        FileStatus.getAllFiles pwd
        |> Seq.map (snd >> FileInfo >> (fun f -> Path.GetRelativePath(pwd, f.Directory.FullName)))
        |> Seq.filter ((<>) ".")

    let dirsFromDiff (spec: DiffSpec) : string seq =

        let currentCommit = spec.CurrentCommit |> Option.defaultValue "HEAD"
        let baseCommit = spec.BaseCommit

        Trace.tracefn $"Current revision: %s{currentCommit}"

        let baseRefs =
            match spec.MaybeTag with
            | Some currentTag ->
                Trace.logfn $"Tag: %s{currentTag}"
                [ git pwd $"describe --abbrev=0 --tags {currentTag}^" ]
            | None ->
                match baseCommit with
                | None
                | Some "0000000000000000000000000000000000000000" ->
                    Trace.tracef "Base revisions(s): "
                    let output = git pwd $$"""show --no-patch --format="%P" {{currentCommit}}"""
                    output.Split ' ' |> Seq.toList
                | Some ref ->
                    Trace.tracefn $"Base revision override: {ref}"
                    [ ref ]

        let dirs =
            seq {
                for baseRef in baseRefs do
                    yield!
                        FileStatus.getChangedFiles pwd currentCommit baseRef
                        |> Seq.map (snd >> FileInfo >> (fun f -> Path.GetRelativePath(pwd, f.Directory.FullName)))
                        |> Seq.filter ((<>) ".")
            }
            |> Seq.distinct

        let info =
            if dirs |> Seq.isEmpty then
                "No meaningful changes detected"
            else
                "Detected git changes in: "

        Trace.tracefn $"%s{info}"
        dirs |> Seq.iter (Trace.logfn "%s")

        dirs
        |> Seq.filter (fun p ->
            if Directory.Exists p then
                true
            else
                Trace.traceImportantfn $"WARNING: path '%s{p}' no longer exists in the repository. Ignoring."
                false)
