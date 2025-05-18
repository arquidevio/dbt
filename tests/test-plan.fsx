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
          kind = "" }

let mergeSelectors =
    test "Merge selectors with the same id" {
        let plan =
            plan {
                profile {
                    selector {
                        id "test"
                        project_id (fun _ -> "hardcoded")
                        exclude "delta"
                    }

                    selector {
                        id "test"
                        exclude "gamma"
                    }

                    selector {
                        id "test"
                        project_id (fun _ -> "last-wins")
                        exclude "zeta"
                    }
                }
            }

        let profiles = "Plan should have some profiles" |> Expect.wantSome plan.profiles

        "Plan should have exactly one profile"
        |> Expect.hasCountOf profiles 1u (fun _ -> true)

        "Plan should contain the default profile"
        |> Expect.exists profiles (fun (KeyValue(k, _)) -> k = defaultProfileId)

        let selectors = profiles[defaultProfileId].selectors

        let selector =
            "The default profile should have exactly one selector"
            |> Expect.wantSome (selectors |> List.tryExactlyOne)

        "Selector id must be 'test'" |> Expect.equal selector.id testSelectorId

        let projectId = ProjectMetadata.Empty |> selector.projectId

        "Project id should be from the last selector"
        |> Expect.equal projectId "last-wins"

        let excludes = selector.patternIgnores

        "Excludes should be merged in the reverse order"
        |> Expect.sequenceEqual excludes [ "zeta"; "gamma"; "delta" ]
    }

let mergeDefaultSelectors =
    test "Merge default selectors" {
        let plan =
            plan {
                profile {
                    selector { pattern "*.json" }
                    selector { pattern "*.yaml" }
                }
            }

        let selector = plan.profiles.Value["default"].selectors |> List.exactlyOne
        "Selector id must be 'default'" |> Expect.equal selector.id defaultSelectorId

        "Pattern should be *.yaml" |> Expect.equal selector.pattern "*.yaml"
    }

let mergeSelectorsExtend =
    test "Merge selectors extend" {
        let baseProfile =
            profile {
                selector {
                    pattern "*.json"
                    exclude "green"
                }
            }

        let plan =
            plan {
                profile {
                    extend baseProfile
                    selector {
                        pattern "*.toml"
                        exclude "blue"
                    }
                }
            }


        let selector = plan.profiles.Value["default"].selectors |> List.exactlyOne
        "Pattern should be *.toml" |> Expect.equal selector.pattern "*.toml"

        "Excludes should be merged in the reverse order"
        |> Expect.sequenceEqual selector.patternIgnores [ "blue"; "green" ]
    }

[<Tests>]
let tests =
    [ mergeSelectors; mergeDefaultSelectors; mergeSelectorsExtend ]
    |> testList "Plan builder"


runTestsWithCLIArgs [] [||] tests
