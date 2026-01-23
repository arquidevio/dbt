#r "paket: nuget Fake.Tools.Git ~> 6.0"

namespace Arquidev.Dbt

#load "types.fsx"
#load "log.fsx"

open Fake.Tools.Git
open System.IO

type BaseCommitStrategy =
    | Override of hash: string
    | Parent
    | MergeBase of targetBranch: string

[<RequireQualifiedAccess>]
module GitDiff =

    let pwd = Directory.GetCurrentDirectory()
    let git = CommandHelper.runSimpleGitCommand

    let allDirs (includeRootDir: bool) : string seq =
        FileStatus.getAllFiles pwd
        |> Seq.map (snd >> FileInfo >> (fun f -> Path.GetRelativePath(pwd, f.Directory.FullName)))
        |> fun xs -> if includeRootDir then xs else xs |> Seq.filter ((<>) ".")

    let dirsFromDiff (includeRootDir: bool) (fromRef: BaseCommitStrategy) (toRef: string option) : DiffResult =

        let currentCommit = toRef |> Option.defaultWith (fun () -> git pwd "rev-parse HEAD")
        let baseCommit = fromRef

        Log.info $"Current revision: %s{currentCommit}"
        Log.info $"Resolving base commit using strategy: %A{fromRef}"

        let baseRefs =
            match baseCommit with
            | Parent
            | Override "0000000000000000000000000000000000000000" ->
                Log.info "Base revisions(s): "
                let output = git pwd $$"""show --no-patch --format="%P" {{currentCommit}}"""
                output.Split ' ' |> Seq.toList
            | Override ref ->
                Log.info $"Base revision override: {ref}"
                [ ref ]
            | MergeBase targetBranch ->
                Log.info "Base revision: "
                git pwd $"fetch origin \"{targetBranch}\"" |> Log.info "%s"

                let output =
                    git pwd $"""merge-base {targetBranch} {currentCommit} """

                [ output.Trim() ]

        let dirs =
            seq {
                for baseRef in baseRefs do
                    yield!
                        FileStatus.getChangedFiles pwd currentCommit baseRef
                        |> Seq.map (snd >> FileInfo >> (fun f -> Path.GetRelativePath(pwd, f.Directory.FullName)))
                        |> fun paths ->
                            if includeRootDir then
                                paths
                            else
                                paths |> Seq.filter ((<>) ".")
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
