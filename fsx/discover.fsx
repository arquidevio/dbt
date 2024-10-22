namespace Arquidev.Dbt

[<RequireQualifiedAccess>]
module internal Discover =
    open System.IO

    /// Find the closest ancestor dir of the originPath that contains a single file matching projectPattern
    let findParentProjectPath (projectPattern: string) (originPath: string) : string option =
        let rec findParentProj (p: string) =
            match Directory.EnumerateFiles(p, projectPattern) |> Seq.tryExactlyOne with
            | None ->
                match Directory.GetParent(p) with
                | null -> None
                | p -> findParentProj p.FullName
            | Some proj -> Some(proj |> Path.GetFullPath)

        findParentProj originPath

    /// Find unique parent projects (determined by existence of a single file matching projectPattern) of the given dirs
    let uniqueParentProjectPaths (projectPattern: string)  (dirs: string seq) : string seq =
        dirs |> Seq.choose (findParentProjectPath projectPattern) |> Seq.distinct
