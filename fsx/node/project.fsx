namespace Arquidev.Dbt

#load "../project.fsx"

[<RequireQualifiedAccess>]
module NodeProject =

    let Selector =
        { Selector.Default with
            kind = "node"
            pattern = "package.json" }
