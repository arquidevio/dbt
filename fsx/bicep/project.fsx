#load "../types.fsx"
#load "../log.fsx"
#load "../plan.fsx"

namespace Arquidev.Dbt

open System.IO
open System.Text.RegularExpressions

[<RequireQualifiedAccess>]
module BicepProject =

    // Matches: module <name> '<path>.bicep' = ...  (in .bicep files)
    let private moduleRef =
        Regex("""module\s+\w+\s+'([^']+\.bicep)'""", RegexOptions.Compiled)

    // Matches: using '<path>.bicep'  (in .bicepparam files)
    let private usingRef = Regex("""using\s+'([^']+\.bicep)'""", RegexOptions.Compiled)

    let parseImports (filePath: string) : string seq =
        let dir = Path.GetDirectoryName(filePath)

        let pattern =
            match Path.GetExtension(filePath) with
            | ".bicepparam" -> usingRef
            | _ -> moduleRef

        try
            File.ReadAllLines(filePath)
            |> Seq.collect (fun line ->
                pattern.Matches(line)
                |> Seq.cast<Match>
                |> Seq.map (fun m -> Path.GetFullPath(Path.Combine(dir, m.Groups.[1].Value))))
        with ex ->
            Log.warn "Failed to parse imports from %s: %s" filePath ex.Message
            Seq.empty

    /// Builds a reverse dependency map: file -> list of files that import it
    let buildReverseDeps (rootDir: string) : Map<string, string list> =
        seq {
            yield! Directory.GetFiles(rootDir, "*.bicep", SearchOption.AllDirectories)
            yield! Directory.GetFiles(rootDir, "*.bicepparam", SearchOption.AllDirectories)
        }
        |> Seq.collect (fun f -> parseImports f |> Seq.map (fun dep -> dep, f))
        |> Seq.groupBy fst
        |> Seq.map (fun (k, vs) -> k, vs |> Seq.map snd |> Seq.toList)
        |> Map.ofSeq

    /// From a changed file, walk the reverse dependency graph and return
    /// all files that match isRequired (i.e. deployable entry points)
    let findAffectedProjects
        (isRequired: string -> bool)
        (reverseDeps: Map<string, string list>)
        (changedFile: string)
        : string seq =

        let rec walk (visited: Set<string>) (file: string) : string seq =
            if visited.Contains(file) then
                Seq.empty
            else
                let visited' = visited.Add(file)

                seq {
                    if isRequired file then
                        yield file

                    for dep in reverseDeps |> Map.tryFind file |> Option.defaultValue [] do
                        yield! walk visited' dep
                }

        walk Set.empty changedFile

[<AutoOpen>]
module BicepSelectors =

    type SelectorBuilderDefaults with
        member _.bicep: BicepSels = BicepSels()

    and BicepSels() =
        member _.generic =
            selector {
                id "bicep"
                pattern "*.bicepparam"
                required_when (fun f -> Path.GetExtension(f) = ".bicepparam")

                expand_leafs (fun ctx ->
                    let rootDir = Directory.GetCurrentDirectory()
                    let reverseDeps = BicepProject.buildReverseDeps rootDir

                    ctx.filesByDir
                    |> Map.values
                    |> Seq.collect (fun x -> x)
                    |> Seq.filter (fun f ->
                        let ext = Path.GetExtension(f)
                        ext = ".bicep" || ext = ".bicepparam")
                    |> Seq.collect (BicepProject.findAffectedProjects ctx.selector.isRequired reverseDeps)
                    |> Seq.distinct)
            }
