namespace Arquidev.Dbt

#load "types.fsx"
#load "env.fsx"
#load "log.fsx"
#load "git-v2.fsx"
#load "ci/github/last-success-sha.fsx"
#load "ce.fsx"

open Arquidev.Dbt

type Plan =
    { profiles: Map<string, Profile> option
      range: Range option }

    static member Default = { profiles = None; range = None }

and Range =
    { fromRef: string option
      toRef: string option }

    static member Default = { fromRef = None; toRef = None }

and Profile =
    { id: string
      selectors: Selector list }

    static member Default = { id = "default"; selectors = [] }

[<RequireQualifiedAccess>]
module Pipeline =
    open System.IO
    open System.Text.RegularExpressions

    /// Find the closest ancestor dir of the originPath that contains a single file matching projectPattern
    let findParentProjectPath
        (projectPattern: string)
        (patternIgnores: string list)
        (originPath: string)
        : string option =
        let rec findParentProj (p: string) =
            match
                Directory.EnumerateFiles(p, projectPattern)
                |> Seq.filter (fun path ->
                    patternIgnores
                    |> Seq.exists (fun pattern -> Regex.IsMatch(path, pattern))
                    |> not)
                |> Seq.tryExactlyOne
            with
            | None ->
                match Directory.GetParent p with
                | null -> None
                | p -> findParentProj p.FullName
            | Some proj -> Some(proj |> Path.GetFullPath)

        findParentProj originPath

    let findRequiredProjects (dirPaths: string seq) (config: Selector) =

        dirPaths
        |> Seq.choose (findParentProjectPath config.pattern config.patternIgnores)
        |> Seq.distinct
        |> Seq.collect (config.expandLeafs config)
        |> Seq.distinct
        |> Seq.filter (not << config.isIgnored)
        |> fun paths ->
            let neitherIgnoredNorRequired =
                paths |> Seq.except (paths |> Seq.filter config.isRequired)

            for path in neitherIgnoredNorRequired do
                Log.warn
                    $"WARNING: %s{path} is a leaf project not matching the inclusion criteria. The project will be ignored."

            paths
        |> Seq.filter config.isRequired
        |> Seq.map (fun p ->
            { kind = config.kind
              path = p
              dir = FileInfo(p).DirectoryName
              safeName = config.safeName p })

[<AutoOpen>]
module rec PlanBuilder =

    type PlanFacet =
        | Profile of Profile
        | Range of Range
        | Custom of Plan
        | RootDir of string

    [<NoComparison; NoEquality>]
    type ProfileBuilder(id: string) =

        member _.Yield(_: unit) : Profile = { Profile.Default with id = id }
        member inline _.Run(p: Profile) = Profile p

        [<CustomOperation("selector")>]
        member inline _.Selector(p: Profile, selector: Selector) =
            { p with
                selectors = selector :: p.selectors }

    [<NoComparison; NoEquality>]
    type RangeBuilder() =
        member inline _.Yield(_: unit) = Range.Default
        member inline _.Run p = Range p

        [<CustomOperation("from_ref")>]
        member inline _.FromRef(range: Range, fromRef: string option) = { range with fromRef = fromRef }

        [<CustomOperation("from_ref")>]
        member inline _.FromRef(range: Range, fromRef: string) = { range with fromRef = Some fromRef }

        [<CustomOperation("to_ref")>]
        member inline _.ToRef(range: Range, toRef: string option) = { range with toRef = toRef }

        [<CustomOperation("to_ref")>]
        member inline _.ToRef(range: Range, toRef: string) = { range with toRef = Some toRef }


    type Ce.CoreBuilder with
        member _.Delay(f: unit -> Plan list) = f () |> List.map Custom
        member _.Yield(state: Plan list) = state |> List.map Custom

        member _.Run state =
            let basePlan =
                { Plan.Default with
                    range =
                        state
                        |> Seq.tryPick (function
                            | Range r -> Some r
                            | _ -> None)
                    profiles =
                        state
                        |> Seq.choose (function
                            | Profile p -> Some(p.id, p)
                            | _ -> None)
                        |> Map.ofSeq
                        |> Some }

            let customPlan =
                state
                |> Seq.tryPick (function
                    | Custom p -> Some p
                    | _ -> None)

            let plan =
                match customPlan with
                | Some c ->
                    { basePlan with
                        range = c.range |> Option.orElse basePlan.range
                        profiles = c.profiles }
                | None -> basePlan

            plan

    let plan = Ce.CoreBuilder()
    let profile id = ProfileBuilder id
    let range = RangeBuilder()

    type Mode =
        | All
        | Diff

    type PipelineOutput =
        { requiredProjects: ProjectPath list
          changeSetRange: ChangeSetRange option }

    and ChangeSetRange =
        { baseCommits: string list
          currentCommit: string }

    type DbtEnv =
        { [<Default("diff")>]
          DBT_MODE: Mode
          [<Default("default")>]
          DBT_PROFILE: string
          DBT_CURRENT_COMMIT: string option
          DBT_BASE_COMMIT: string option }

    [<RequireQualifiedAccess>]
    module Plan =

        let env =
            Lazy<DbtEnv>(fun () ->
                let env: DbtEnv = Env.get<DbtEnv> ()

                { env with
                    DBT_BASE_COMMIT =
                        env.DBT_BASE_COMMIT
                        |> Option.orElseWith (fun () -> LastSuccessSha.getLastSuccessCommitHash () |> _.toOption) })

        let evaluate (plan: Plan) : PipelineOutput =

            Log.info "DBT Build Plan"
        
            let env = env.Value

            let plan =
                PlanBuilder.plan {
                    range {
                        from_ref env.DBT_BASE_COMMIT
                        to_ref env.DBT_CURRENT_COMMIT
                    }

                    plan
                }

            Log.debug "%A" plan
            Log.info $"Mode: %s{env.DBT_MODE.ToString().ToLower()}"
            Log.info $"Profile: %s{env.DBT_PROFILE}"

            let mode = env.DBT_MODE
            let profile = env.DBT_PROFILE

            let dirs, diffRange =
                match mode with
                | Diff ->
                    Log.header "GIT CHANGE SET"

                    let result =
                        plan.range
                        |> Option.map (fun r -> r.fromRef, r.toRef)
                        |> Option.defaultValue ((None, None))
                        ||> GitDiff.dirsFromDiff

                    result.dirs,
                    Some
                        { baseCommits = result.effectiveRange.baseCommits
                          currentCommit = result.effectiveRange.currentCommit }
                | All -> GitDiff.allDirs (), None

            { requiredProjects =
                match plan.profiles with
                | Some p when p |> Map.count > 0 && p.ContainsKey profile -> p[profile].selectors
                | _ -> failwithf $"Profile {profile} not configured"
                |> Seq.collect (Pipeline.findRequiredProjects dirs)
                |> Seq.toList
              changeSetRange = diffRange }
