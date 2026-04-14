#load "../../fsx/ci/github/output.fsx"
#load "../../fsx/ci/github/deploy-spec.fsx"

open Arquidev.Dbt

let makeSpec (keys: int) (imgs: int) =
  let changeKeys = [ for i in 1..keys -> sprintf "TICKET-%06d" i ]

  let requiredProjects =
    [ for i in 1..imgs do
        { fileName = sprintf "project-%03d.json" i
          fileNameNoExtension = sprintf "project-%03d" i
          fullPath = sprintf "/services/service-%03d/project-%03d.json" i i
          fullDir = sprintf "/services/service-%03d" i
          dir = sprintf "service-%03d" i
          dirSlug = sprintf "service-%03d" i
          relativePath = sprintf "services/service-%03d/project-%03d.json" i i
          relativeDir = sprintf "services/service-%03d" i
          projectId = sprintf "service-%03d" i
          kind = "docker" } ]

  { requiredProjects = requiredProjects
    changeKeys = Some changeKeys
    changeSetRange = None
    changedDirs = None }

// (label, change_keys count, images count)
let cases =
  [ "small", 10, 5
    "medium", 100, 50
    "large", 500, 200
    "xlarge", 1000, 500 ]

for label, keys, imgs in cases do
  let planOutput = makeSpec keys imgs
  let spec = GitHubDeploymentSpec.Plan.deploymentSpec planOutput
  let jsonStr = Json.write spec
  printfn "[%s] keys=%d imgs=%d json-bytes=%d" label keys imgs jsonStr.Length

  planOutput
  |> Output.Plan.appendToGithubOutputJson (sprintf "spec-%s" label) (fun _ -> spec)
  |> Output.Plan.appendToGithubOutput (sprintf "expected-%s" label) (string keys)
  |> Output.Plan.appendToGithubOutput (sprintf "json-len-%s" label) (string jsonStr.Length)
  |> ignore
