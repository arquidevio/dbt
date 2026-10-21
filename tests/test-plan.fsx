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

    "Patterns should not be duplicated" |> Expect.equal selector.patterns.Length 2

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

    let derived = plan { profile { selector { extend baseSelector } } }

    let patterns = derived.profiles.Value["default"].selector.Value.patterns

    "Each pattern should appear exactly once"
    |> Expect.containsAll patterns [ "*.bicep"; "*.bicepparam" ]

    "Patterns should not be duplicated" |> Expect.equal patterns.Length 2
  }

let summaryPrintsAfterEvaluate =
  testSequenced
  <| test "Summary prints after the log sink is disposed" {
    (Arquidev.Tools.Log.Console.enableWithLevel Arquidev.Tools.Log.Level.info).Dispose()

    let out = new System.IO.StringWriter()
    let stdout = System.Console.Out
    System.Console.SetOut out

    try
      Plan.summary
        { requiredProjects =
            [ { ProjectMetadata.Empty with
                  projectId = "app"
                  fullPath = "/repo/app/app.fsproj" } ]
          changeKeys = None
          changeSetRange = None
          changedDirs = None }
    finally
      System.Console.SetOut stdout

    "Summary lists the project"
    |> Expect.stringContains (out.ToString()) "> app /repo/app/app.fsproj"
  }

[<Tests>]
let tests =
  [ mergeSelectorsExtend; extendDoesNotDuplicatePatterns; summaryPrintsAfterEvaluate ]
  |> testList "Plan builder"


runTestsWithCLIArgs [] [||] tests
