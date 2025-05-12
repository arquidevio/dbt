#load "../types.fsx"
#load "../plan.fsx"

namespace Arquidev.Dbt


[<AutoOpen>]
module NodeSelectors =

    type selector with
        static member node: Selectors = Selectors()

    and Selectors() =
        member _.image = selector.define "node" { pattern "package.json" }
