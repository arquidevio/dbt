namespace Arquidev.Dbt

#load "types.fsx"
#load "log.fsx"
#load "json.fsx"

open Arquidev.Tools
open System.IO

type private SnapshotRecord =
    { changeSetRange: ChangeSetRange option
      changedDirs: Map<string, string list>
      requiredProjects: ProjectMetadata list }

[<RequireQualifiedAccess>]
module Snapshot =

    let private toRecord (output: PlanOutput) : SnapshotRecord =
        { changeSetRange = output.changeSetRange
          changedDirs = output.changedDirs |> Option.defaultValue Map.empty
          requiredProjects = output.requiredProjects }

    let private baseName (output: PlanOutput) =
        match output.changeSetRange with
        | Some r ->
            let baseHash = r.baseCommits |> List.head |> (fun h -> h.[0..6])
            let currentHash = r.currentCommit.[0..6]
            $".dbt-{baseHash}-{currentHash}"
        | None -> ".dbt-all"

    let private filePath (output: PlanOutput) =
        Path.Combine(Directory.GetCurrentDirectory(), $"{baseName output}.snapshot.json")

    let private missFilePath (output: PlanOutput) =
        Path.Combine(Directory.GetCurrentDirectory(), $"{baseName output}.snapshot-miss.json")

    let apply (output: PlanOutput) =
        let env = readEnv<{| DBT_SNAPSHOT: SnapshotMode option |}> ()

        match env.DBT_SNAPSHOT with
        | None -> ()

        | Some Write ->
            Log.header "SNAPSHOT WRITE"
            let snap = toRecord output
            let path = filePath output
            File.WriteAllText(path, Json.writePretty snap)
            Log.info $"Snapshot written to: %s{path}"

        | Some Validate ->
            Log.header "SNAPSHOT VALIDATE"
            let path = filePath output

            if not (File.Exists path) then
                Log.error $"Snapshot file not found: %s{path}"
                exit 1

            let saved = File.ReadAllText path |> Json.read<SnapshotRecord>
            let current = toRecord output

            if saved = current then
                Log.info "Snapshot matches current output"
            else
                let missPath = missFilePath output
                File.WriteAllText(missPath, Json.writePretty current)
                Log.error $"Snapshot mismatch. Current output written to: %s{missPath}"
                exit 1
