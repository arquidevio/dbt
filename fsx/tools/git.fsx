#r "paket:
  nuget Fake.Tools.Git ~> 6.0
  nuget Semver ~> 3.0
"

#load "../regex.fsx"

namespace Arquidev.Dbt

open Regex
open Fake.Core
open Fake.Tools
open Semver
open System.IO

[<RequireQualifiedAccess>]
module Git =

    type GitRef =
        { Hash: string
          Name: string
          IsCommit: bool }

    type TagInfo =
        { CommitShortHash: string
          CommitMessage: string
          CommitDate: string
          SemVer: SemVersion }

    type RepoHasNoTags = exn

    type Repo(repoDir: string) =

        member _.AddRemote url =
            Git.CommandHelper.gitCommandf repoDir $"remote add origin %s{url}"

        member _.CheckoutTag tag =
            Git.Branches.checkoutBranch repoDir $"tags/%s{tag}"

        member _.Checkout branch =
            Git.Branches.checkoutBranch repoDir branch

        member _.CheckoutNew source branch =
            Git.Branches.checkoutNewBranch repoDir source branch

        member _.Commit (author: string option) (messages: string list) =
            let msgString = messages |> List.map (sprintf "-m \"%s\"") |> String.concat " "

            let authorFlag =
                author |> Option.map (sprintf "--author \"%s\"") |> Option.defaultValue ""

            $"""commit {authorFlag} {msgString}"""
            |> Git.CommandHelper.runSimpleGitCommand repoDir
            |> Trace.trace

        member _.PushBranch branch =
            Git.CommandHelper.gitCommandf repoDir $"push --set-upstream origin %s{branch}"

        member _.PushOrigin() =
            Git.CommandHelper.gitCommandf repoDir "push origin"

        member _.FetchTags() =
            Git.CommandHelper.gitCommand repoDir "fetch --tags --force"

        member _.ShowRefTags() =
            Git.CommandHelper.getGitResult repoDir "show-ref --tags -d"
            |> Seq.map (fun x ->
                let chunks = x.Split(" ")

                { Hash = chunks.[0]
                  Name =
                    match chunks.[1] with
                    | ParseRegex "refs/tags/([^^{}]*)(^{})?" [ x; _ ] -> x
                    | _ -> failwithf "Invalid ref %s" chunks.[1]
                  IsCommit = chunks.[1].EndsWith("^{}") })

        member _.Pull() =
            Git.CommandHelper.gitCommand repoDir "pull"

        member this.PullBranch() =
            Git.CommandHelper.gitCommand repoDir $"pull origin {this.CurrentBranch()}"

        member _.CurrentBranch() = Git.Information.getBranchName repoDir

        member _.IsDirty() =
            Git.Information.isCleanWorkingCopy repoDir |> not

        member _.HasStagedChanges() =

            let success, _, _ =
                Git.CommandHelper.runGitCommand repoDir "diff --staged --no-ext-diff --quiet"

            not success

        member _.CurrentHash() =
            Git.Information.getCurrentSHA1 repoDir |> Git.Information.showName repoDir

        member _.StageAll() = Git.Staging.stageAll repoDir

        member _.Stage(paths: string list) =
            for p in paths do
                Git.Staging.stageFile repoDir p |> ignore

        member _.Diff filePath refA refB =
            Git.CommandHelper.showGitCommand
                repoDir
                (sprintf "--no-pager diff --color --exit-code %s %s -- %s" refA refB filePath)

        member _.DiffNameOnly refA refB =
            Git.CommandHelper.getGitResult repoDir (sprintf "--no-pager diff --name-only --exit-code %s %s" refA refB)

        member _.DiffLocalFiles pathA pathB =
            (Git.CommandHelper.getGitResult
                repoDir
                (sprintf "--no-pager diff --color --no-index --exit-code -- %s %s" pathA pathB))
            |> Seq.iter (printfn "%s")

        member _.HttpsUrl() =
            let url = Git.CommandHelper.getGitResult repoDir "config --get remote.origin.url"

            match url.Head with
            | ParseRegex "git@(.*):(.*).git" [ uri; path ] -> sprintf "https://%s/%s" uri path
            | _ -> failwithf "Not an SSH repo URL: %s" url.Head

    let getLastSemVerTag repoUrl =
        let output =
            Git.CommandHelper.getGitResult "." (sprintf "ls-remote --refs --tags %s" repoUrl)

        let tag =
            match output with
            | [] -> raise (RepoHasNoTags repoUrl)
            | ls ->
                ls
                |> Seq.choose (function
                    | ParseRegex ".*\/([^\/]+)$" [ tag ] -> Some(SemVersion.Parse(tag, SemVersionStyles.Strict))
                    | _ -> None)
                |> Seq.sortWith _.CompareSortOrderTo
                |> Seq.last

        tag.ToString()

    let getRecentTags repoUrl =

        let repoPath = "/tmp/" + Path.GetTempFileName()

        try
            Directory.CreateDirectory repoPath |> ignore

            Git.CommandHelper.gitCommandf repoPath "init -q"
            let repo = Repo(repoPath)
            repo.AddRemote repoUrl
            repo.FetchTags()

            let cmd =
                "log -n 5 --no-walk --tags --decorate-refs=tags --pretty=\"%h,\t%D,\t%s,\t%cd\""

            let lines = Git.CommandHelper.getGitResult repoPath cmd

            let parseLog =
                function
                | ParseRegex "^([a-z0-9]{7}),\ttag: (.*),\t(.*),\t(.*)$" [ shortHash; tag; subject; date ] ->
                    Some
                        { SemVer = SemVersion.Parse(tag.Split(",").[0], SemVersionStyles.Any)
                          CommitShortHash = shortHash
                          CommitMessage = subject
                          CommitDate = date }
                | _ -> None

            match lines with
            | [] -> raise (RepoHasNoTags repoUrl)
            | ls -> ls |> Seq.choose parseLog
        finally
            Directory.Delete(repoPath, true)
