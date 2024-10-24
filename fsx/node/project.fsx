namespace Arquidev.Dbt

#load "../types.fsx"

[<RequireQualifiedAccess>]
module NodeProject =

    let Selector =
        { Selector.Default with
            kind = "node"
            pattern = "package.json" }
