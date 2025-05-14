namespace Arquidev.Dbt

#load "types.fsx"
#load "env.fsx"
#load "log.fsx"
#load "git.fsx"
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
      changeKeyPrefixRegex: (string * string option) option
      postActions: (PlanOutput -> unit) list
      selectors: Selector list }

    static member Default =
        { id = "default"
          changeKeyPrefixRegex = None
          postActions = []
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
                  dir = file.Directory.Name
                  dirSlug = file.Directory.Name.ToLowerInvariant().Replace(".", "-")
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
        | BasePlan of Plan

    type ProfileFacet =
        | ProfileId of string
        | BaseProfile of Profile
        | Selector of Selector
        | ChangeKeyRegex of regex: string * replacement: string option
        | PostAction of (PlanOutput -> unit)

    type SelectorFacet =
        | SelectorId of string
        | BaseSelector of Selector
        | Pattern of string
        | Exclude of string
        | RequiredWhen of (string -> bool)
        | IgnoredWhen of (string -> bool)
        | ProjectId of (ProjectMetadata -> string)
        | ExpandLeafs of (Selector -> string -> string seq)

    [<NoComparison; NoEquality>]
    type SelectorBuilder() =
        inherit Ce.CoreBuilder()

        [<CustomOperation("id")>]
        member inline _.Id(state, id: string) = [ SelectorId id ] @ state

        [<CustomOperation("pattern")>]
        member inline _.Pattern(state, pattern: string) = [ Pattern pattern ] @ state

        [<CustomOperation("exclude")>]
        member inline _.Exclude(state, exclude: string) = [ Exclude exclude ] @ state

        [<CustomOperation("required_when")>]
        member inline _.RequiredWhen(state, isRequired: string -> bool) = [ RequiredWhen isRequired ] @ state

        [<CustomOperation("ignored_when")>]
        member inline _.IgnoredWhen(state, isIgnored: string -> bool) = [ IgnoredWhen isIgnored ] @ state

        [<CustomOperation("project_id")>]
        member inline _.ProjectId(state, projectId: ProjectMetadata -> string) = [ ProjectId projectId ] @ state

        [<CustomOperation("expand_leafs")>]
        member inline _.ExpandLeafs(state, expandLeafs: Selector -> string -> string seq) =
            [ ExpandLeafs expandLeafs ] @ state

        [<CustomOperation("extend")>]
        member inline _.Extend(state, defaults: Selector) = [ BaseSelector defaults ] @ state

        member _.Run(state: SelectorFacet list) : Selector =

            let tryPick f = state |> List.tryPick f

            let defaults =
                tryPick (function
                    | BaseSelector x -> Some x
                    | _ -> None)
                |> Option.defaultValue Selector.Default

            let id =
                tryPick (function
                    | SelectorId x -> Some x
                    | _ -> None)

            let pattern =
                tryPick (function
                    | Pattern x -> Some x
                    | _ -> None)

            let excludes =
                state
                |> List.choose (function
                    | Exclude x -> Some x
                    | _ -> None)

            let requiredWhen =
                tryPick (function
                    | RequiredWhen f -> Some f
                    | _ -> None)

            let ignoredWhen =
                tryPick (function
                    | IgnoredWhen f -> Some f
                    | _ -> None)

            let projectId =
                tryPick (function
                    | ProjectId f -> Some f
                    | _ -> None)

            let expandLeafs =
                tryPick (function
                    | ExpandLeafs f -> Some f
                    | _ -> None)

            { defaults with
                id = id |> Option.defaultValue defaults.id
                pattern = pattern |> Option.defaultValue defaults.pattern
                patternIgnores = excludes
                isRequired = requiredWhen |> Option.defaultValue defaults.isRequired
                isIgnored = ignoredWhen |> Option.defaultValue defaults.isIgnored
                projectId = projectId |> Option.defaultValue defaults.projectId
                expandLeafs = expandLeafs |> Option.defaultValue defaults.expandLeafs }

    [<NoComparison; NoEquality>]
    type ProfileBuilder() =
        inherit Ce.CoreBuilder()

        member _.Delay(f: unit -> Selector) = [ f () |> Selector ]
        member _.Yield(state: Selector) = [ state |> Selector ]

        [<CustomOperation("change_key_regex")>]
        member inline _.ChangeKeyRegex(state, regex: string, ?replacement: string) =
            [ ChangeKeyRegex(regex, replacement) ] @ state

        [<CustomOperation("post_action")>]
        member inline _.PostAction(state, action: PlanOutput -> unit) = [ PostAction action ] @ state

        [<CustomOperation("id")>]
        member inline _.Id(state, id: string) = [ ProfileId id ] @ state

        [<CustomOperation("extend")>]
        member inline _.Extend(state, defaults: Profile) = [ BaseProfile defaults ] @ state

        member _.Run(state: ProfileFacet list) =

            let defaults =
                state
                |> List.tryPick (function
                    | BaseProfile x -> Some x
                    | _ -> None)
                |> Option.defaultValue Profile.Default

            let id =
                state
                |> List.tryPick (function
                    | ProfileId x -> Some x
                    | _ -> None)

            { id = id |> Option.defaultValue "default"
              changeKeyPrefixRegex =
                state
                |> List.tryPick (function
                    | ChangeKeyRegex(regex, replace) -> Some(regex, replace)
                    | _ -> None)
              postActions =
                defaults.postActions
                @ (state
                   |> List.choose (function
                       | PostAction x -> Some x
                       | _ -> None))
              selectors =
                defaults.selectors
                @ (state
                   |> List.choose (function
                       | Selector id -> Some id
                       | _ -> None))
                |> List.filter (fun s -> defaults.selectors |> List.exists (fun l -> l.id = s.id) |> not) }


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

        member _.Delay(f: unit -> Profile) = [ f () |> Profile ]
        member _.Yield(state: Profile) = [ state |> Profile ]

        [<CustomOperation("extend")>]
        member inline _.Extend(state, defaults: Plan) = [ BasePlan defaults ] @ state

        member _.Run state =
            let defaults =
                state
                |> Seq.tryPick (function
                    | BasePlan p -> Some p
                    | _ -> None)
                |> Option.defaultValue Plan.Default

            let profiles =
                let newProfiles =
                    state
                    |> Seq.choose (function
                        | Profile p -> Some(p.id, p)
                        | _ -> None)
                    |> Map.ofSeq

                match defaults.profiles with
                | None -> newProfiles
                | Some d -> Map.ofList [ yield! d |> Map.toSeq; yield! newProfiles |> Map.toSeq ]

            { range =
                state
                |> Seq.tryPick (function
                    | Range r -> Some r
                    | _ -> None)
              profiles = Some profiles }

    let range = RangeBuilder()
    let profile = ProfileBuilder()
    let selector = SelectorBuilder()
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
                match plan.range with
                | None ->
                    { plan with
                        range =
                            Some
                                { fromRef = env.DBT_BASE_COMMIT
                                  toRef = env.DBT_CURRENT_COMMIT } }
                | Some _ -> plan

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
                        |> Option.map (fun (regex, _) ->
                            r.baseCommits
                            |> Seq.collect (fun baseCommit ->
                                Git.Repo(".").ParseCommitMessage baseCommit r.currentCommit regex)
                            |> Seq.distinct
                            |> Seq.toList))
                    |> Option.flatten }

            Log.debug "%A" result

            if result.requiredProjects.Length = 0 then
                Log.info "No project changes. Exiting"
                exit 0

            for action in profile.postActions do
                action result

            result

        let summary (output: PlanOutput) =
            Log.header "REQUIRED PROJECTS"

            for p in output.requiredProjects do
                Log.info $"> {p.projectId} {p.fullPath}"
