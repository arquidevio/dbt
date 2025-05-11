#r "paket: nuget Fake.Tools.Git ~> 6.0"
#load "log.fsx"

namespace Arquidev.Dbt

open Fake.Tools.Git
open System.IO

type DiffResult =
    { effectiveRange: EffectiveDiffRange
      dirs: string seq }

and EffectiveDiffRange =
    { baseCommits: string list
      currentCommit: string }

[<RequireQualifiedAccess>]
module GitDiff =

    let pwd = Directory.GetCurrentDirectory()
    let git = CommandHelper.runSimpleGitCommand

    let allDirs () : string seq =
        FileStatus.getAllFiles pwd
        |> Seq.map (snd >> FileInfo >> (fun f -> Path.GetRelativePath(pwd, f.Directory.FullName)))
        |> Seq.filter ((<>) ".")

    let dirsFromDiff (fromRef: string option) (toRef: string option) : DiffResult =

        let currentCommit = toRef |> Option.defaultWith (fun () -> git pwd "rev-parse HEAD")
        let baseCommit = fromRef

        Log.info $"Current revision: %s{currentCommit}"

        let baseRefs =
            match baseCommit with
            | None
            | Some "0000000000000000000000000000000000000000" ->
                Log.info "Base revisions(s): "
                let output = git pwd $$"""show --no-patch --format="%P" {{currentCommit}}"""
                output.Split ' ' |> Seq.toList
            | Some ref ->
                Log.info $"Base revision override: {ref}"
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
                "No relevant changes detected"
            else
                "Detected git changes in: "

        Log.info $"%s{info}"
        dirs |> Seq.iter (Log.info "%s")

        let dirs =
            dirs
            |> Seq.filter (fun p ->
                if Directory.Exists p then
                    true
                else
                    Log.warn $"WARNING: path '%s{p}' no longer exists in the repository. Ignoring."
                    false)

        { dirs = dirs
          effectiveRange =
            { baseCommits = baseRefs
              currentCommit = currentCommit } }
