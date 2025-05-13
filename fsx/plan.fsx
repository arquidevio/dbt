namespace Arquidev.Dbt

#load "types.fsx"
#load "env.fsx"
#load "log.fsx"
#load "git-v2.fsx"
#load "tools/git.fsx"
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
      changeKeyPrefixRegex: string option
      selectors: Selector list }

    static member Default =
        { id = "default"
          changeKeyPrefixRegex = None
          selectors = [] }

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

            let patternFile = projectPattern |> Path.GetFileName
            let patternDir = projectPattern |> Path.GetDirectoryName

            match
                Directory.EnumerateFiles(p, patternFile)
                |> Seq.filter _.StartsWith(patternDir)
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
            let file = FileInfo p
            let cwd = Directory.GetCurrentDirectory()
            let relativeDir = Path.GetRelativePath(cwd, file.DirectoryName)

            let output =
                { kind = config.id
                  fileName = file.Name
                  fullPath = p
                  fullDir = file.DirectoryName
                  relativePath = Path.GetRelativePath(cwd, p)
                  projectId =
                    relativeDir.ToLowerInvariant()
                    |> fun p -> p.Replace(" ", "-")
                    |> fun p -> p.Split [| '.'; '_' |] |> String.concat "-" }

            { output with
                projectId = config.projectId output })

[<AutoOpen>]
module rec PlanBuilder =

    type PlanFacet =
        | Profile of Profile
        | Range of Range
        | Custom of Plan

    type ProfileFacet =
        | Selector of Selector
        | ChangeKeyRegex of string

    [<NoComparison; NoEquality>]
    type SelectorBuilder(?id: string, ?defaults: Selector) =

        [<CustomOperation("pattern")>]
        member inline _.Pattern(state, pattern: string) = { state with pattern = pattern }

        [<CustomOperation("exclude")>]
        member inline _.Exclude(state, exclude: string) =
            { state with
                patternIgnores = exclude :: state.patternIgnores }

        [<CustomOperation("required_when")>]
        member inline _.RequiredWhen(state, isRequired: string -> bool) = { state with isRequired = isRequired }

        [<CustomOperation("ignored_when")>]
        member inline _.IgnoredWhen(state, isIgnored: string -> bool) = { state with isIgnored = isIgnored }

        [<CustomOperation("project_id")>]
        member inline _.ProjectId(state: Selector, projectId: ProjectMetadata -> string) =
            { state with projectId = projectId }

        [<CustomOperation("expand_leafs")>]
        member inline _.ExpandLeafs(state, expandLeafs: Selector -> string -> string seq) =
            { state with expandLeafs = expandLeafs }

        member inline _.Delay(f: unit -> Selector) = f ()

        member _.Yield(state: unit) =
            let result = defaults |> Option.defaultValue Selector.Default

            match id with
            | Some id -> { result with id = id }
            | None -> result

        member _.Run(state: Selector) : Selector = state


    [<NoComparison; NoEquality>]
    type ProfileBuilder(id: string) =
        inherit Ce.CoreBuilder()
        member _.Delay(f: unit -> Selector) = [ f () |> Selector ]
        member _.Yield(state: Selector) = [ state |> Selector ]

        [<CustomOperation("change_key_regex")>]
        member inline _.ChangeKeyRegex(state, regex: string) = [ ChangeKeyRegex regex ] @ state

        member _.Run(state: ProfileFacet list) =

            Profile
                { Profile.Default with
                    id = id
                    changeKeyPrefixRegex =
                        state
                        |> List.tryPick (function
                            | ChangeKeyRegex x -> Some x
                            | _ -> None)
                    selectors =
                        state
                        |> List.choose (function
                            | Selector id -> Some id
                            | _ -> None) }


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

    type PlanBuilder() =
        inherit Ce.CoreBuilder()
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

    let range = RangeBuilder()

    let profile id = ProfileBuilder id
    let default_profile = ProfileBuilder "default"

    type selector =
        static member define id = SelectorBuilder id
        static member extend defaults = SelectorBuilder(defaults = defaults)

    let plan = PlanBuilder()

    type Mode =
        | All
        | Diff

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

        let evaluate (plan: Plan) : PlanOutput =

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

            let profile =
                match plan.profiles with
                | Some p when p |> Map.count > 0 && p.ContainsKey profile -> p[profile]
                | _ -> failwithf $"Profile {profile} not configured"

            let result =
                { requiredProjects =
                    profile.selectors
                    |> Seq.collect (Pipeline.findRequiredProjects dirs)
                    |> Seq.toList
                  changeSetRange = diffRange
                  changeKeys =
                    diffRange
                    |> Option.map (fun r ->
                        profile.changeKeyPrefixRegex
                        |> Option.map (fun key ->
                            r.baseCommits
                            |> Seq.collect (fun baseCommit ->
                                Git.Repo(".").ParseCommitMessage baseCommit r.currentCommit key)
                            |> Seq.distinct
                            |> Seq.toList))
                    |> Option.flatten }

            Log.debug "%A" result
            result
