#load "../types.fsx"
#load "../log.fsx"
#load "../plan.fsx"

namespace Arquidev.Dbt

open System.IO
open System.Text.RegularExpressions

[<RequireQualifiedAccess>]
module BicepProject =

  let private moduleRef =
    Regex("""module\s+\w+\s+'([^']+\.bicep)'""", RegexOptions.Compiled)

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

  let makeDependencyTree (rootDir: string) : Map<string, string list> =
    seq {
      yield! Directory.GetFiles(rootDir, "*.bicep", SearchOption.AllDirectories)
      yield! Directory.GetFiles(rootDir, "*.bicepparam", SearchOption.AllDirectories)
    }
    |> Seq.collect (fun f -> parseImports f |> Seq.map (fun dep -> dep, f))
    |> Seq.groupBy fst
    |> Seq.map (fun (k, vs) -> k, vs |> Seq.map snd |> Seq.toList)
    |> Map.ofSeq

  let findLeafDependants
    (projs: Map<string, string list>)
    (isLeafProject: string -> bool)
    (changedFile: string)
    : string seq =

    let rec walk (visited: Set<string>) (file: string) : string seq =
      if visited.Contains(file) then
        Seq.empty
      else
        let visited' = visited.Add(file)

        seq {
          if isLeafProject file then
            yield file

          for dep in projs |> Map.tryFind file |> Option.defaultValue [] do
            yield! walk visited' dep
        }

    walk Set.empty changedFile

[<AutoOpen>]
module BicepSelectors =

  type SelectorBuilderDefaults with
    member _.bicep: Selectors = Selectors()

  and Selectors() =
    member _.generic =
      selector {
        id "bicep"
        pattern "*.bicep"
        pattern "*.bicepparam"
        required_when (fun f -> Path.GetExtension(f) = ".bicepparam")

        expand_leafs (fun ctx ->
          let rootDir = Directory.GetCurrentDirectory()
          let dependencyTree = BicepProject.makeDependencyTree rootDir

          ctx.filesByDir
          |> Map.values
          |> Seq.collect (fun x -> x)
          |> Seq.filter (fun f ->
            let ext = Path.GetExtension(f)
            ext = ".bicep" || ext = ".bicepparam")
          |> Seq.collect (BicepProject.findLeafDependants dependencyTree ctx.selector.isRequired)
          |> Seq.distinct)
      }
