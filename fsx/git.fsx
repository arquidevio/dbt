#r "paket:
  nuget Fake.Tools.Git = 6.1.1
  nuget Fake.Core.Environment = 6.1.1
  nuget Fake.Core.Trace = 6.1.1"

namespace Arquidev.Dbt

open Fake.Core
open Fake.Tools.Git
open System.IO

[<RequireQualifiedAccess>]
module Git =

    let env = Environment.environVarOrDefault
    let pwd = Directory.GetCurrentDirectory()
    let git = CommandHelper.runSimpleGitCommand

    let uniqueDirsWithChanges () : string seq =

        let currentRef = env "BUILD_CURRENT_REF" "HEAD"
        let baseRefOverride = Environment.environVarOrNone "BUILD_BASE_REF"

        Trace.tracefn $"Current ref: %s{currentRef}"

        let baseRefs =
            let maybeTag = Environment.environVarOrNone "MAYBE_TAG"

            match maybeTag with
            | Some currentTag ->
                Trace.logfn $"Building tag: %s{currentTag}"
                [ git pwd $"describe --abbrev=0 --tags {currentTag}^" ]
            | None ->
                match baseRefOverride with
                | Some ref ->
                    Trace.tracefn $"Base ref set via $BUILD_BASE_REF: {ref}"
                    [ ref ]
                | None ->
                    Trace.tracef "Base ref(s): "
                    let output = git pwd $$"""show --no-patch --format="%P" {{currentRef}}"""
                    output.Split(' ') |> Seq.toList

        let dirs =
            seq {
                for baseRef in baseRefs do
                    yield!
                        FileStatus.getChangedFiles pwd currentRef baseRef
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
