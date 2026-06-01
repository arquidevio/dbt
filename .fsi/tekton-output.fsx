#load "../fsx/ci/tekton/output.fsx"

open Arquidev.Dbt

let planOutput =
  { requiredProjects = []
    changeKeys = None
    changeSetRange = None
    changedDirs = None }


Plan.writeToTektonResultArray "TEST_ARRAY" (fun _ -> [ "d"; "b"; "f" ]) planOutput
