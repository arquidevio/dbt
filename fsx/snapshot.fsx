namespace Arquidev.Dbt

#load "types.fsx"
#load "log.fsx"
#load "json.fsx"

open Arquidev.Tools
open System.IO

/// Portable snapshot of a single selected project — no absolute paths.
type private SnapshotProject =
  { projectId: string
    kind: string
    fileName: string
    relativePath: string
    relativeDir: string
    dir: string
    dirSlug: string }

type private SnapshotRecord =
  { changeSetRange: ChangeSetRange option
    changedDirs: Map<string, string list>
    requiredProjects: SnapshotProject list }

[<RequireQualifiedAccess>]
module Snapshot =

  let private toSnapshotProject (p: ProjectMetadata) : SnapshotProject =
    { projectId = p.projectId
      kind = p.kind
      fileName = p.fileName
      relativePath = p.relativePath
      relativeDir = p.relativeDir
      dir = p.dir
      dirSlug = p.dirSlug }

  let private toRecord (output: PlanOutput) : SnapshotRecord =
    { changeSetRange = output.changeSetRange
      changedDirs = output.changedDirs |> Option.defaultValue Map.empty
      requiredProjects = output.requiredProjects |> List.map toSnapshotProject }

  let private baseName (profile: string) (output: PlanOutput) =
    match output.changeSetRange with
    | Some r ->
      let baseHash = r.baseCommits |> List.head |> (fun h -> h.[0..6])
      let currentHash = r.currentCommit.[0..6]
      $".dbt-{profile}-{baseHash}-{currentHash}"
    | None -> $".dbt-{profile}-all"

  let private filePath (profile: string) (output: PlanOutput) =
    Path.Combine(Directory.GetCurrentDirectory(), $"{baseName profile output}.snapshot.json")

  let private missFilePath (profile: string) (output: PlanOutput) =
    Path.Combine(Directory.GetCurrentDirectory(), $"{baseName profile output}.snapshot-miss.json")

  let apply (output: PlanOutput) =
    let env = readEnv<{| DBT_SNAPSHOT: SnapshotMode option; DBT_PROFILE: string option |}> ()
    let profile = env.DBT_PROFILE |> Option.defaultValue "default"

    match env.DBT_SNAPSHOT with
    | None -> ()

    | Some Write ->
      Log.header "SNAPSHOT WRITE"
      let snap = toRecord output
      let path = filePath profile output
      File.WriteAllText(path, Json.writePretty snap)
      Log.info $"Snapshot written to: %s{path}"

    | Some Validate ->
      Log.header "SNAPSHOT VALIDATE"
      let path = filePath profile output

      if not (File.Exists path) then
        Log.error $"Snapshot file not found: %s{path}"
        exit 1

      let saved = File.ReadAllText path |> Json.read<SnapshotRecord>
      let current = toRecord output

      if saved = current then
        Log.info "Snapshot matches current output"
      else
        let missPath = missFilePath profile output
        File.WriteAllText(missPath, Json.writePretty current)
        Log.error $"Snapshot mismatch. Current output written to: %s{missPath}"
        exit 1
