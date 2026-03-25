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

  let pwd () =
    CommandHelper.runSimpleGitCommand (Directory.GetCurrentDirectory()) $"rev-parse --show-toplevel"

  let allDirs (includeRootDir: bool) : Map<string, string list> =
    let cwd = pwd ()

    FileStatus.getAllFiles cwd
    |> Seq.map (fun (_, filePath) ->
      let dir = Path.GetRelativePath(cwd, (FileInfo filePath).Directory.FullName)
      dir, filePath)
    |> fun pairs ->
        if includeRootDir then
          pairs
        else
          pairs |> Seq.filter (fun (dir, _) -> dir <> ".")
    |> Seq.groupBy fst
    |> Seq.map (fun (dir, pairs) -> dir, pairs |> Seq.map snd |> Seq.toList)
    |> Map.ofSeq

  let dirsFromDiff (includeRootDir: bool) (fromRef: BaseCommitStrategy) (toRef: string option) : DiffResult =
    let cwd = pwd ()
    let git = CommandHelper.runSimpleGitCommand cwd

    let currentCommit = toRef |> Option.defaultWith (fun () -> git "rev-parse HEAD")
    let baseCommit = fromRef

    Log.info $"Current revision: %s{currentCommit}"
    Log.info $"Resolving base commit using strategy: %A{fromRef}"

    let baseRefs =
      match baseCommit with
      | Parent
      | Override "0000000000000000000000000000000000000000" ->
        Log.info "Base revisions(s): "
        let output = git $$"""show --no-patch --format="%P" {{currentCommit}}"""
        output.Split ' ' |> Seq.toList
      | Override ref ->
        let resolved = (git $"rev-parse {ref}").Trim()
        Log.info $"Base revision override: {ref} -> {resolved}"
        [ resolved ]
      | MergeBase targetBranch ->
        Log.info "%s" (git $"fetch origin {targetBranch}:refs/remotes/origin/{targetBranch}")

        let output = git $"""merge-base origin/{targetBranch} {currentCommit} """
        let ref = output.Trim()
        Log.info $"Base revision: {ref}"
        [ ref ]

    let dirs =
      seq {
        for baseRef in baseRefs do
          yield!
            FileStatus.getChangedFiles cwd currentCommit baseRef
            |> Seq.map (fun (_, filePath) ->
              let dir = Path.GetRelativePath(cwd, (FileInfo filePath).Directory.FullName)
              dir, filePath)
            |> fun pairs ->
                if includeRootDir then
                  pairs
                else
                  pairs |> Seq.filter (fun (dir, _) -> dir <> ".")
      }
      |> Seq.groupBy fst
      |> Seq.map (fun (dir, pairs) -> dir, pairs |> Seq.map snd |> Seq.distinct |> Seq.toList)
      |> Map.ofSeq

    let info =
      if dirs |> Map.isEmpty then
        "No relevant changes detected"
      else
        "Detected git changes in: "

    Log.info $"%s{info}"
    dirs |> Map.iter (fun dir _ -> Log.info "%s" dir)

    let dirs =
      dirs
      |> Map.filter (fun dir _ ->
        if Path.Combine(cwd, dir) |> Directory.Exists then
          true
        else
          Log.warn $"WARNING: path '%s{dir}' no longer exists in the repository. Ignoring."
          false)

    { dirs = dirs
      effectiveRange =
        { baseCommits = baseRefs
          currentCommit = currentCommit } }
