#load "../types.fsx"
#load "../plan.fsx"

namespace Arquidev.Dbt


[<AutoOpen>]
module NodeSelectors =

    type SelectorBuilderDefaults with
        member _.node: Selectors = Selectors()

    and Selectors() =
        member _.image =
            selector {
                id "node"
                pattern "package.json"
            }
