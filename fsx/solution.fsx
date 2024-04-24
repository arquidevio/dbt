#r "nuget: Ionide.ProjInfo, 0.64.0"
#r "nuget: Fake.Core.Trace, 6.0.0"
#r "nuget: Fake.Core.Process, 6.0.0"

#nowarn "57"

namespace Arquidev.Dbt

open Fake.Core
open Ionide.ProjInfo.InspectSln
open System.Xml.XPath
open System.Collections.Generic
open System.IO
open System

[<RequireQualifiedAccess>]
module Solution =

    let private getSlnData slnPath =
        let result = tryParseSln slnPath

        let _, data =
            match result with
            | Ok x -> x
            | Error e -> failwith e.Message

        data

    let findInCwd () : string =
        Directory.EnumerateFiles "."
        |> Seq.map FileInfo
        |> Seq.tryFind (fun f -> f.Extension = ".sln")
        |> Option.defaultWith (fun () -> failwith $"Sln file not found")
        |> fun f -> f.FullName
        |> IO.Path.GetFullPath

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

    let findRequiredProjects (slnPath: string) (projectFilter) (dirs: string seq) =
        let projs = makeDependencyTree slnPath

        dirs
        |> Seq.filter (fun p ->
            if Directory.Exists(p) then
                true
            else
                Trace.traceImportantfn $"WARNING: path '%s{p}' no longer exists in the repository. Ignoring."
                false)

        |> Seq.choose (fun d ->
            let rec findParentProj (path: string) =
                match Directory.EnumerateFiles(path, "*.*sproj") |> Seq.toList with
                | [] ->
                    match Directory.GetParent(path) with
                    | null -> None
                    | p -> findParentProj p.FullName
                | [ proj ] -> Some proj
                | _ -> failwithf $"Found multiple project files in %s{d}"

            findParentProj d)
        |> Seq.map Path.GetFullPath
        |> Seq.distinct
        //|> Seq.map(fun x -> printfn ">>>%s" x;x)
        |> Seq.collect (findLeafDependants projs projectFilter)
        |> Seq.distinct
        |> Seq.toList

    let generateRestoreList (projectFilter: string -> bool) (slnPath: string) : unit =
        let pwd = Path.GetDirectoryName slnPath
        let input = StreamRef.Empty

        let tar =
            CreateProcess.fromRawCommand
                "tar"
                [ "--sort=name"
                  "--owner=root:0"
                  "--group=root:0"
                  "--mtime=2023-01-01 00:00:00"
                  "-czvf"
                  "restore-list.tar.gz"
                  "-T"
                  "-" ]
            |> CreateProcess.withStandardInput (CreatePipe input)
            |> Proc.start

        findProjects projectFilter slnPath
        |> Seq.map (fun path -> path.Replace(pwd, String.Empty).Trim('/'))
        |> Seq.iter (fun path -> input.Value.Write(Text.Encoding.UTF8.GetBytes(path + Environment.NewLine)))

        input.Value.Flush()
        input.Value.Close()
        tar.Wait()
