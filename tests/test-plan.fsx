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
      fileNameNoExtension = ""
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

    "Patterns should contain both without duplication"
    |> Expect.containsAll selector.patterns [ "*.json"; "*.toml" ]

    "Patterns should not be duplicated"
    |> Expect.equal selector.patterns.Length 2

    "Excludes should be merged in the reverse order"
    |> Expect.sequenceEqual selector.excludePatterns [ "blue"; "green" ]
  }

let extendDoesNotDuplicatePatterns =
  test "Extend does not duplicate patterns" {
    let baseSelector =
      selector {
        id "base"
        pattern "*.bicep"
        pattern "*.bicepparam"
      }

    let derived =
      plan {
        profile {
          selector {
            extend baseSelector
          }
        }
      }

    let patterns = derived.profiles.Value["default"].selector.Value.patterns

    "Each pattern should appear exactly once"
    |> Expect.containsAll patterns [ "*.bicep"; "*.bicepparam" ]

    "Patterns should not be duplicated"
    |> Expect.equal patterns.Length 2
  }

[<Tests>]
let tests =
  [ mergeSelectorsExtend; extendDoesNotDuplicatePatterns ]
  |> testList "Plan builder"


runTestsWithCLIArgs [] [||] tests
