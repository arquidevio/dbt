#load "../types.fsx"

namespace Arquidev.Dbt

[<RequireQualifiedAccess>]
module NodeProject =

    let Selector =
        { Selector.Default with
            id = "node"
            pattern = "package.json" }
