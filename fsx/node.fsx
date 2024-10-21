namespace Arquidev.Dbt

#load "discover.fsx"

[<RequireQualifiedAccess>]
module Node =

    let findParentProjects (dirs: string seq) =
        Discover.uniqueParentProjectPaths dirs "package.json" |> Seq.toList

