#r "paket: nuget Expecto ~> 10"

#load "../fsx/dotnet/project.fsx"
#load "../fsx/plan.fsx"

open Expecto
open Arquidev.Dbt

[<Literal>]
let defaultProfileId = "default"

[<Literal>]
let testSelectorId = "test"

[<Literal>]
let defaultSelectorId = "default"

type ProjectMetadata with
    static member Empty =
        { projectId = ""
          fileName = ""
          fullPath = ""
          fullDir = ""
          dir = ""
          dirSlug = ""
          relativePath = ""
          kind = ""
          relativeDir = "" }

let mergeSelectorsExtend =
    test "Merge selectors extend" {
        let baseProfile =
            profile {
                selector {
                    id "my-selector"
                    pattern "*.json"
                    exclude "green"
                }
            }

        let plan =
            plan {
                profile {
                    include_root_dir true
                    extend baseProfile

                    selector {
                        pattern "*.toml"
                        exclude "blue"
                    }
                }
            }


        let selector = plan.profiles.Value["default"].selector.Value
        "Pattern should be *.toml" |> Expect.equal selector.pattern "*.toml"

        "Excludes should be merged in the reverse order"
        |> Expect.sequenceEqual selector.patternIgnores [ "blue"; "green" ]
    }

[<Tests>]
let tests = [ mergeSelectorsExtend ] |> testList "Plan builder"


runTestsWithCLIArgs [] [||] tests
