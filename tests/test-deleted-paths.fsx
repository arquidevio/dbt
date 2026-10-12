#r "paket: nuget Expecto ~> 10"

#load "../fsx/plan.fsx"
#load "../fsx/kustomize/project.fsx"

open Expecto
open Arquidev.Dbt
open System.IO

let withRepo (f: string -> unit) =
  let root =
    Path.Combine(Directory.GetCurrentDirectory(), $"tmp-dbt-test-{System.Guid.NewGuid():N}")

  let write (path: string) (content: string) =
    let full = Path.Combine(root, path)
    Directory.CreateDirectory(Path.GetDirectoryName full) |> ignore
    File.WriteAllText(full, content)

  write "envs/prod/kustomization.yaml" "resources:\n- ../../component\n"
  write "base/kustomization.yaml" "resources: []\n"

  try
    f root
  finally
    Directory.Delete(root, true)

let kustomizeSelector (root: string) =
  let tree = KustomizeProject.makeDependencyTree root

  selector {
    id "kustomize"
    pattern "kustomization.yaml"
    discovery_root root
    required_when (fun dir -> dir = Path.Combine(root, "envs/prod"))

    expand_leafs (fun ctx ->
      ctx.projectPath
      |> Path.GetDirectoryName
      |> KustomizeProject.findLeafDependants tree ctx.selector.isRequired)
  }
  |> makeSelector

let deletedFile (root: string) (dir: string) =
  let full = Path.Combine(root, dir)
  full, [ Path.Combine(full, "kustomization.yaml") ]

let requiredFor (root: string) (deletedDir: string) =
  findRequiredProjects { filesByDir = Map.ofList [ deletedFile root deletedDir ] } false (kustomizeSelector root)
  |> Seq.map _.fullPath
  |> Seq.toList

let deletedComponentTriggersDependants =
  test "Deleted component still referenced triggers its dependants" {
    withRepo (fun root ->
      "Dependant overlay should be required"
      |> Expect.equal (requiredFor root "component") [ Path.Combine(root, "envs/prod") ])
  }

let deletedTopLevelProjectIsIgnored =
  test "Deleted top-level project is ignored" {
    withRepo (fun root -> "Nothing to validate" |> Expect.isEmpty (requiredFor root "envs/dev"))
  }

let deletedNestedDirTriggersParentProject =
  test "Deleted dir nested in an existing project triggers the project" {
    withRepo (fun root ->
      let dir = Path.Combine(root, "base/patches")

      "Parent project should be required"
      |> Expect.equal
        (findRequiredProjects
          { filesByDir = Map.ofList [ dir, [ Path.Combine(dir, "patch.yaml") ] ] }
          false
          { kustomizeSelector root with
              isRequired = fun _ -> true }
         |> Seq.map _.fullPath
         |> Seq.toList)
        [ Path.Combine(root, "base") ])
  }

[<Tests>]
let tests =
  [ deletedComponentTriggersDependants
    deletedTopLevelProjectIsIgnored
    deletedNestedDirTriggersParentProject ]
  |> testList "Deleted paths"

runTestsWithCLIArgs [] [||] tests
