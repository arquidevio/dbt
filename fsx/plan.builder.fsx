namespace Arquidev.Dbt

#r "paket: nuget Arquidev.Log ~> 0.3.0"

#load "types.fsx"

open Arquidev.Tools

[<AutoOpen>]
module rec PlanBuilder =

  let private log = Log.Source "Arquidev.Dbt.PlanBuilder"

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
    | DiscoveryRoot of string
    | RequiredWhen of (string -> bool)
    | IgnoredWhen of (string -> bool)
    | ProjectId of (ProjectMetadata -> string)
    | ExpandLeafs of (LeafExpansionContext -> string seq)

  type SelectorBuilderDefaults() = class end


  type CoreBuilder() =
    member _.Zero() : 'a list = []
    member _.Delay(f: unit -> 'a list) = f ()
    member _.Yield(x: 'a list) = x
    member _.Yield(x: 'a) = [ x ]
    member _.Yield(x: unit) = []
    member _.Combine(a, b) = a @ b
    member _.For(xs, f: unit -> 'a list) = (xs |> Seq.toList) @ f ()


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
        log.trace "%A" state
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

    let output =
      output
      |> List.filter (function
        | BaseSelector _ -> false
        | _ -> true)

    log.trace "SELECTOR BUILDER RUN: %A -> %A" state output
    output

  [<NoComparison; NoEquality>]
  type SelectorBuilder() =
    inherit CoreBuilder()

    member _.defaults: SelectorBuilderDefaults = SelectorBuilderDefaults()

    [<CustomOperation("id")>]
    member _.Id(state, id: string) = [ SelectorId id ] @ state

    /// <summary>
    /// Glob pattern for matching project files
    /// </summary>
    /// <example>
    /// <code>
    /// selector {
    ///     pattern "*.fsproj"
    /// }
    /// </code>
    /// </example>
    [<CustomOperation("pattern")>]
    member _.Pattern(state, pattern: string) = [ Pattern pattern ] @ state

    /// <summary>
    /// Glob exclusion patterns to filter out unwanted projects discovered by the selector pattern
    /// </summary>
    /// <remarks>
    /// * Patterns are relative to the current working dir
    /// * Use multiple times to define multiple excludes
    /// </remarks>
    /// <example>
    /// <code>
    /// selector {
    ///     exclude "subdirectory"
    ///     exclude "other-dir/subdir"
    /// }
    /// </code>
    /// </example>
    [<CustomOperation("exclude")>]
    member _.Exclude(state, exclude: string) = [ Exclude exclude ] @ state

    /// <summary>
    /// Determines if a detected project is required
    /// </summary>
    /// <remarks>
    /// Default: true
    /// </remarks>
    [<CustomOperation("required_when")>]
    member _.RequiredWhen(state, isRequired: string -> bool) = [ RequiredWhen isRequired ] @ state

    /// <summary>
    /// Determines if a detected project should be ignored
    /// </summary>
    /// <remarks>
    /// Default: false
    /// </remarks>
    [<CustomOperation("ignored_when")>]
    member _.IgnoredWhen(state, isIgnored: string -> bool) = [ IgnoredWhen isIgnored ] @ state

    /// <summary>
    /// Overrides the root directory used for project discovery (default: cwd)
    /// </summary>
    [<CustomOperation("discovery_root")>]
    member _.DiscoveryRoot(state, path: string) = [ DiscoveryRoot path ] @ state

    /// <summary>
    /// Overrides the default project id generation function
    /// </summary
    [<CustomOperation("project_id")>]
    member _.ProjectId(state, projectId: ProjectMetadata -> string) = [ ProjectId projectId ] @ state

    /// <summary>
    /// Determines if/how to traverse dependency tree to determine the leaf projects
    /// </summary>
    /// <remarks>
    /// * The default strategy is no dependency traversal
    /// * Some built-in selectors, like the dotnet selector have a strategy implemented
    /// </remarks>
    [<CustomOperation("expand_leafs")>]
    member _.ExpandLeafs(state, expandLeafs: LeafExpansionContext -> string seq) =
      [ ExpandLeafs expandLeafs ] @ state

    /// <summary>
    /// Can be used to extend and customize an already existing selector (e.g. a built-in one)
    /// </summary>
    [<CustomOperation("extend")>]
    member _.Extend(state, defaults: SelectorFacet list) =
      let output = [ BaseSelector defaults ] @ state
      log.debug "SELECTOR EXTEND: %A -> %A" state output
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

    let patterns =
      state
      |> List.choose (function
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

    let discoveryRoot =
      tryPick (function
        | DiscoveryRoot x -> Some x
        | _ -> None)

    let expandLeafs =
      tryPick (function
        | ExpandLeafs f -> Some f
        | _ -> None)

    let output =
      Selector.Default
      |> fun s -> id |> Option.map (fun x -> { s with id = x }) |> Option.defaultValue s
      |> fun s ->
          discoveryRoot
          |> Option.map (fun x -> { s with discoveryRoot = Some x })
          |> Option.defaultValue s
      |> fun s ->
          { s with
              patterns = s.patterns @ patterns }
      |> fun s ->
          { s with
              excludePatterns = s.excludePatterns @ excludes }
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

    log.trace "Make selector: %A -> %A" originalState output
    output

  [<NoComparison; NoEquality>]
  type ProfileBuilder() =
    inherit CoreBuilder()

    // this was causing an empty selector list being added
    //member _.Delay(f: unit -> SelectorFacet list) = [ f () |> Selector ]
    member _.Yield(state: SelectorFacet list) = [ state |> Selector ]

    [<CustomOperation("change_key_regex")>]
    member _.ChangeKeyRegex(state, regex: string, ?replacement: string) =
      [ ChangeKeyRegex(regex, replacement) ] @ state

    [<CustomOperation("post_action")>]
    member _.PostAction(state, action: PlanOutput -> unit) = [ PostAction action ] @ state

    [<CustomOperation("id")>]
    member _.Id(state, id: string) = [ ProfileId id ] @ state

    [<CustomOperation("include_root_dir")>]
    member _.IncludeRootDir(state, value: bool) = [ IncludeRootDir value ] @ state


    [<CustomOperation("extend")>]
    member _.Extend(state, defaults: ProfileFacet list) = [ BaseProfile defaults ] @ state

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

    log.trace "Make profile: %A -> %A" state output
    output

  [<NoComparison; NoEquality>]
  type RangeBuilder() =
    member _.Yield(_: unit) = Range.Default
    member _.Run p = Range p

    [<CustomOperation("from_ref")>]
    member _.FromRef(range: Range, fromRef: string option) = { range with fromRef = fromRef }

    [<CustomOperation("from_ref")>]
    member _.FromRef(range: Range, fromRef: string) = { range with fromRef = Some fromRef }

    [<CustomOperation("to_ref")>]
    member _.ToRef(range: Range, toRef: string option) = { range with toRef = toRef }

    [<CustomOperation("to_ref")>]
    member _.ToRef(range: Range, toRef: string) = { range with toRef = Some toRef }

  type PlanBuilder() =
    inherit CoreBuilder()

    //member _.Delay(f: unit -> ProfileFacet list) = [ f () |> makeProfile |> Profile ]
    member _.Yield(state: ProfileFacet list) = [ state |> makeProfile |> Profile ]

    [<CustomOperation("extend")>]
    member _.Extend(state, defaults: Plan) = [ BasePlan defaults ] @ state

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
