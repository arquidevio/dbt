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
      includeRootDir: bool
      changeKeyPrefixRegex: (string * string option) option
      postActions: (PlanOutput -> unit) list
      selector: Selector option }

    static member Default =
        { id = "default"
          includeRootDir = false
          changeKeyPrefixRegex = None
          postActions = []
          selector = None }

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
                  relativeDir = Path.GetRelativePath(cwd, p) |> Path.GetDirectoryName
                  dir = file.Directory.Name
                  dirSlug = file.Directory.Name.ToLowerInvariant().Replace(".", "-")
                  projectId =
                    relativeDir.ToLowerInvariant()
                    |> fun p -> p.Replace(" ", "-")
                    |> fun p -> p.Split [| '.'; '_' |] |> String.concat "-" }

            { output with
                projectId = config.projectId output })
        |> Seq.sortBy (fun p -> p.projectId)

[<AutoOpen>]
module rec PlanBuilder =

    type PlanFacet =
        | Profile of Profile
        | Range of Range
        | BasePlan of Plan

    type ProfileFacet =
        | ProfileId of string
        | IncludeRootDir of bool
        | BaseProfile of ProfileFacet list
        | Selector of SelectorFacet list
        | ChangeKeyRegex of regex: string * replacement: string option
        | PostAction of (PlanOutput -> unit)

    type SelectorFacet =
        | SelectorId of string
        | BaseSelector of SelectorFacet list
        | Pattern of string
        | Exclude of string
        | RequiredWhen of (string -> bool)
        | IgnoredWhen of (string -> bool)
        | ProjectId of (ProjectMetadata -> string)
        | ExpandLeafs of (Selector -> string -> string seq)

    type SelectorBuilderDefaults() = class end

    let tryGetSelectorId (state: SelectorFacet list) =
        state
        |> List.choose (function
            | SelectorId id -> Some id
            | _ -> None)
        |> List.tryExactlyOne

    let flattenSelector (state: SelectorFacet list) : SelectorFacet list =
        let baseSelector =
            match
                state
                |> List.choose (function
                    | BaseSelector b -> Some b
                    | _ -> None)
            with
            | [ s ] -> Some s
            | [] -> None
            | _ ->
                Log.trace "%A" state
                failwithf "extend can be only called once"

        let output =

            match baseSelector with
            | None -> state
            | Some baseState ->
                let baseId = tryGetSelectorId baseState
                let currentId = tryGetSelectorId state

                match baseId, currentId with
                | b, c when b = c -> baseState @ state
                | Some b, None -> baseState @ SelectorId b :: state
                | _ -> failwithf "Invalid extend config"

        Log.trace "SELECTOR BUILDER RUN: %A -> %A" state output
        output

    [<NoComparison; NoEquality>]
    type SelectorBuilder() =
        inherit Ce.CoreBuilder()

        member _.defaults: SelectorBuilderDefaults = SelectorBuilderDefaults()

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
        member inline _.Extend(state, defaults: SelectorFacet list) =
            let output = [ BaseSelector defaults ] @ state
            Log.debug "SELECTOR EXTEND: %A -> %A" state output
            output

        member _.Run(state: SelectorFacet list) : SelectorFacet list = flattenSelector state


    let makeSelector (state: SelectorFacet list) =

        let originalState = state
        let state = state |> List.rev
        let tryPick f = state |> List.tryPick f

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

        let output =
            Selector.Default
            |> fun s -> id |> Option.map (fun x -> { s with id = x }) |> Option.defaultValue s
            |> fun s -> pattern |> Option.map (fun x -> { s with pattern = x }) |> Option.defaultValue s
            |> fun s ->
                { s with
                    patternIgnores = s.patternIgnores @ excludes }
            |> fun s ->
                requiredWhen
                |> Option.map (fun x -> { s with isRequired = x })
                |> Option.defaultValue s
            |> fun s ->
                ignoredWhen
                |> Option.map (fun x -> { s with isIgnored = x })
                |> Option.defaultValue s
            |> fun s ->
                projectId
                |> Option.map (fun x -> { s with projectId = x })
                |> Option.defaultValue s
            |> fun s ->
                expandLeafs
                |> Option.map (fun x -> { s with expandLeafs = x })
                |> Option.defaultValue s

        Log.trace "Make selector: %A -> %A" originalState output
        output

    [<NoComparison; NoEquality>]
    type ProfileBuilder() =
        inherit Ce.CoreBuilder()

        // this was causing an empty selector list being added
        //member _.Delay(f: unit -> SelectorFacet list) = [ f () |> Selector ]
        member _.Yield(state: SelectorFacet list) = [ state |> Selector ]

        [<CustomOperation("change_key_regex")>]
        member inline _.ChangeKeyRegex(state, regex: string, ?replacement: string) =
            [ ChangeKeyRegex(regex, replacement) ] @ state

        [<CustomOperation("post_action")>]
        member inline _.PostAction(state, action: PlanOutput -> unit) = [ PostAction action ] @ state

        [<CustomOperation("id")>]
        member inline _.Id(state, id: string) = [ ProfileId id ] @ state

        [<CustomOperation("include_root_dir")>]
        member inline _.IncludeRootDir(state, value: bool) = [ IncludeRootDir value ] @ state


        [<CustomOperation("extend")>]
        member inline _.Extend(state, defaults: ProfileFacet list) = [ BaseProfile defaults ] @ state

        member _.Run(state: ProfileFacet list) =

            state
            |> List.collect (fun f ->
                match f with
                | BaseProfile x -> x
                | x -> [ x ])

    let makeProfile (state: ProfileFacet list) =

        let profileId =
            state
            |> List.tryPick (function
                | ProfileId x -> Some x
                | _ -> None)

        let includeRootDir =
            state
            |> List.tryPick (function
                | IncludeRootDir x -> Some x
                | _ -> None)

        let changeKeyPrefixRegex =
            state
            |> List.tryPick (function
                | ChangeKeyRegex(regex, replace) -> Some(regex, replace)
                | _ -> None)

        let postActions =
            state
            |> List.choose (function
                | PostAction x -> Some x
                | _ -> None)

        let selectorFacets =
            state
            |> List.choose (function
                | Selector s -> Some s
                | _ -> None)
            |> List.collect flattenSelector

        let output =
            Profile.Default
            |> fun s -> profileId |> Option.map (fun x -> { s with id = x }) |> Option.defaultValue s
            |> fun s ->
                includeRootDir
                |> Option.map (fun x -> { s with includeRootDir = x })
                |> Option.defaultValue s
            |> fun s ->
                changeKeyPrefixRegex
                |> Option.map (fun x -> { s with changeKeyPrefixRegex = Some x })
                |> Option.defaultValue s
            |> fun s ->
                { s with
                    postActions = s.postActions @ postActions }
            |> fun s ->
                { s with
                    selector = Some(makeSelector selectorFacets) }

        Log.trace "Make profile: %A -> %A" state output
        output

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

        //member _.Delay(f: unit -> ProfileFacet list) = [ f () |> makeProfile |> Profile ]
        member _.Yield(state: ProfileFacet list) = [ state |> makeProfile |> Profile ]

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

            Log.trace "%A" plan
            Log.info $"Mode: %s{env.DBT_MODE.ToString().ToLower()}"
            Log.info $"Profile: %s{env.DBT_PROFILE}"

            let mode = env.DBT_MODE
            let profileId = env.DBT_PROFILE

            let profile =
                match plan.profiles with
                | Some p when p |> Map.count > 0 && p.ContainsKey profileId -> p[profileId]
                | _ -> failwithf $"Profile {profileId} not configured"


            let dirs, diffRange =
                match mode with
                | Diff ->
                    Log.header "GIT CHANGE SET"

                    let result =
                        plan.range
                        |> Option.map (fun r -> profile.includeRootDir, r.fromRef, r.toRef)
                        |> Option.defaultValue ((profile.includeRootDir, None, None))
                        |||> GitDiff.dirsFromDiff

                    result.dirs,
                    Some
                        { baseCommits = result.effectiveRange.baseCommits
                          currentCommit = result.effectiveRange.currentCommit }
                | All -> GitDiff.allDirs profile.includeRootDir, None


            let selector =
                match profile.selector with
                | Some s -> s
                | _ -> failwithf $"No selectors configured"

            let result =
                { requiredProjects = selector |> Pipeline.findRequiredProjects dirs |> Seq.toList
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

            Log.trace "%A" result

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
