#r "paket: 
      nuget Ionide.ProjInfo ~> 0.70
      nuget Fake.Core.Process ~> 6.0
"

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

        let data =
            match result with
            | Ok x -> x
            | Error e -> failwith e.Message

        data

    let findInDir (dir: string) : string =
        Directory.EnumerateFiles dir
        |> Seq.map FileInfo
        |> Seq.tryFind (fun f -> f.Extension = ".sln")
        |> Option.defaultWith (fun () -> failwith $"Sln file not found")
        |> fun f -> f.FullName
        |> IO.Path.GetFullPath

    let findInCwd () : string =
        findInDir (Directory.GetCurrentDirectory())

    let findProjects (projectFilter: string -> bool) (slnPath: string) =

        let data = getSlnData slnPath

        let rec projs (item: SolutionItem) =
            match item.Kind with
            | MSBuildFormat _ ->
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
            | MSBuildFormat _ -> [ getProjReferenceDeps item.Name |> Seq.map (fun k -> (k, item.Name)) ]
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
        (isLeafProject: string -> bool)
        (projectPath: string)
        =
        let rec find (sofar: string list) (proj: string) : string list =
            if projs.ContainsKey(proj) && not <| isLeafProject proj then
                projs[proj] |> Seq.collect (find sofar) |> Seq.toList
            else
                proj :: sofar

        find [] projectPath |> Seq.distinct

    let generateRestoreList (slnPath: string) : unit =
        let slnDir = Path.GetDirectoryName slnPath
        let originalPwd = Directory.GetCurrentDirectory()

        try
            Directory.SetCurrentDirectory slnDir
            let input = StreamRef.Empty

            let tar =
                CreateProcess.fromRawCommand
                    "tar"
                    [ "--sort=name"
                      "--owner=root:0"
                      "--group=root:0"
                      "--mtime=2023-01-01 00:00:00"
                      "-czvf"
                      Path.Combine(slnDir, "restore-list.tar.gz")
                      "-T"
                      "-" ]
                |> CreateProcess.withStandardInput (CreatePipe input)
                |> Proc.start

            findProjects (fun _ -> true) slnPath
            |> Seq.map (fun path -> path.Replace(slnDir, String.Empty).Trim('/'))
            |> Seq.iter (fun path -> input.Value.Write(Text.Encoding.UTF8.GetBytes(path + Environment.NewLine)))

            input.Value.Flush()
            input.Value.Close()
            tar.Wait()
        finally
            Directory.SetCurrentDirectory originalPwd
