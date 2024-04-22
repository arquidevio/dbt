#r "nuget: Ionide.ProjInfo, 0.61.3"
#nowarn "57"

open Ionide.ProjInfo.InspectSln
open System.Xml.XPath
open System.Collections.Generic
open System.IO
open System

let private getSlnData slnPath =
    let result = tryParseSln slnPath

    let _, data =
        match result with
        | Ok x -> x
        | Error e -> failwith e.Message

    data

let projSafeName (projFullPath: string) =
    projFullPath
    |> Path.GetDirectoryName
    |> Path.GetFileName
    |> fun p -> p.ToLowerInvariant()
    |> fun p -> p.Split('.')
    |> fun p -> p[1..]
    |> fun p -> String.Join('-', p)
//|> fun p -> printfn "%s" p; p // DEBUG

let propertyProjectFilter (propertyName: string) (projPath: string) : bool =
    let xp = XPathDocument(projPath)
    let n = xp.CreateNavigator()

    n.SelectSingleNode($"/Project/PropertyGroup[*]/{propertyName}[text()='true']")
    |> (not << isNull)

let findProjects (projectFilter: string -> bool) (slnPath: string) =

    let data = getSlnData slnPath

    let rec projs (item: SolutionItem) =
        match item.Kind with
        | MsbuildFormat _ ->
            [ match projectFilter item.Name with
              | true -> Some item.Name
              | _ -> None ]
        | Folder(items, _) -> items |> List.collect projs
        | Unsupported
        | Unknown -> [ None ]

    data.Items |> List.collect projs |> Seq.choose id |> Seq.toList

let makeDependencyTree (slnPath: string) =

    let getProjReferenceDeps (projPath: string) : HashSet<string> =

        let resolveFullPath (relativePath: string) =
            Path.GetFullPath(Path.Combine(Path.GetDirectoryName(projPath), relativePath.Replace("\\", "/")))

        let xp = XPathDocument(projPath)
        let n = xp.CreateNavigator()
        let references = n.Select("//ProjectReference")
        let mutable projectReferences = HashSet<string>()

        while references.MoveNext() do

            let referenceVal =
                match references.Current.SelectSingleNode("@Include") with
                | null -> references.Current.SelectSingleNode("Include").Value
                | v -> v.Value

            projectReferences.Add(referenceVal |> resolveFullPath) |> ignore

        projectReferences

    let rec projs (item: SolutionItem) =
        match item.Kind with
        | MsbuildFormat _ -> [ getProjReferenceDeps item.Name |> Seq.map (fun k -> (k, item.Name)) ]
        | Folder(items, _) -> items |> List.collect projs
        | Unsupported
        | Unknown -> []

    let data = getSlnData slnPath

    data.Items
    |> List.collect projs
    |> Seq.collect id
    |> Seq.groupBy fst
    |> Seq.map (fun (k, g) -> (k, g |> Seq.map snd |> Seq.toList))
    |> dict

let findLeafDependants
    (projs: IDictionary<string, list<string>>)
    (leafProjPredicate: string -> bool)
    (projectPath: string)
    =
    let rec find (sofar: string list) (proj: string) : string list =
        if projs.ContainsKey(proj) && not <| leafProjPredicate proj then
            projs[proj] |> Seq.collect (find sofar) |> Seq.toList
        else
            proj :: sofar

    find [] projectPath |> Seq.distinct
