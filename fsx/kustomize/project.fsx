#load "../types.fsx"
#load "../plan.fsx"
#load "../yaml.fsx"

[<RequireQualifiedAccess>]
module KustomizeProject =
  open System.IO
  open System.Collections.Generic
  open Arquidev.Dbt

  type Kustomization =
    { resources: string list option
      components: string list option
      generators: string list option
      transformers: string list option }

  let makeDependencyTree (rootPath: string) =

    let getProjReferenceDeps (projDir: string) : HashSet<string> =

      let resolveFullPath (relativePath: string) =
        Path.Combine(projDir, relativePath.Replace("\\", "/"))
        |> Path.GetFullPath
        |> Path.TrimEndingDirectorySeparator

      let kustomization =
        Yaml.read<Kustomization> (File.ReadAllText $"{projDir}/kustomization.yaml")

      HashSet<string>(
        [ yield! kustomization.resources |> Option.defaultValue []
          yield! kustomization.components |> Option.defaultValue []
          yield! kustomization.generators |> Option.defaultValue []
          yield! kustomization.transformers |> Option.defaultValue [] ]
        |> List.filter (not << Path.HasExtension)
        |> List.map resolveFullPath
      )

    let allKustomizations () =
      Directory.GetFiles(rootPath, "kustomization.yaml", SearchOption.AllDirectories)
      |> Seq.map Path.GetDirectoryName
      |> Seq.toList


    let rec traverse (projPath: string) =
      [ getProjReferenceDeps projPath |> Seq.map (fun k -> k, projPath) ]

    allKustomizations ()
    |> List.collect traverse
    |> Seq.collect id
    |> Seq.groupBy fst
    |> Seq.map (fun (k, g) -> k, g |> Seq.map snd |> Seq.toList)
    |> dict

  let findLeafDependants
    (projs: IDictionary<string, list<string>>)
    (isLeafProject: string -> bool)
    (projectPath: string)
    =
    let rec find (sofar: string list) (proj: string) : string list =

      if projs.ContainsKey proj && not <| isLeafProject proj then
        projs[proj] |> Seq.collect (find sofar) |> Seq.toList
      else
        proj :: sofar

    find [] projectPath |> Seq.distinct

[<AutoOpen>]
module KustomizeSelectors =
  open System.IO
  open Arquidev.Dbt

  let cwd = Directory.GetCurrentDirectory()

  let dependencyTree = KustomizeProject.makeDependencyTree cwd

  type SelectorBuilderDefaults with
    member _.kustomize: Selectors = Selectors()

  and Selectors() =
    member _.generic =
      selector {
        id "kustomize"
        pattern "kustomization.yaml"
        required_when (fun dir -> true)

        expand_leafs (fun ctx ->
          ctx.projectPath
          |> Path.GetDirectoryName
          |> KustomizeProject.findLeafDependants dependencyTree ctx.selector.isRequired)
      }
