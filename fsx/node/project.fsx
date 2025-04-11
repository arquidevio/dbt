#load "../types.fsx"

namespace Arquidev.Dbt

[<RequireQualifiedAccess>]
module NodeProject =

    let Selector =
        { Selector.Default with
            kind = "node"
            pattern = "package.json" }
