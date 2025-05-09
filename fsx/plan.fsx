namespace Arquidev.Dbt

#load "types.fsx"
#load "log.fsx"
#load "env.fsx"
#load "git-v2.fsx"
#load "ci/github/last-success-sha.fsx"
#load "experiment.fsx"
#load "dotnet/project.fsx"


open Arquidev.Dbt


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
module Plan =

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

        //  output: Output }

        static member Default =
            { id = "default"
              //     output = fun _ -> ()
              selectors = [] }

    //and Output = ProjectPath -> obj

    [<NoComparison; NoEquality>]
    type ProfileBuilder(id: string) =

        // [<CustomOperation("output")>]
        // member inline _.Output<'a>(p: Profile, [<InlineIfLambdaAttribute>] output: ProjectPath -> 'a) =
        //     { p with
        //         output = fun p -> box<'a> (output p) }

        member this.Yield _ : Profile = { Profile.Default with id = id }
        member inline _.Run p : Profile = p


        [<CustomOperation("selector")>]
        member inline _.Selector(p: Profile, selector: Selector) =
            { p with
                selectors = selector :: p.selectors }




    [<NoComparison; NoEquality>]
    type RangeBuilder() =


        member this.Yield _ : Range = Range.Default
        member _.Zero() = Range.Default

        [<CustomOperation("from_ref")>]
        member inline _.FromRef(range: Range, fromRef: string option) = { range with fromRef = fromRef }

        [<CustomOperation("to_ref")>]
        member inline _.ToRef(range: Range, toRef: string option) = { range with toRef = toRef }

        member inline _.Run p : Range = p


    type PlanFacet =
        | Profile of Profile
        | Range of Range
        | Custom of Plan

    type PlanBuilder() =

        member inline _.Zero() = ()
        member inline _.Yield(_: unit) = ()
        member inline _.Yield(x: Range) = PlanFacet.Range x
        member inline _.Yield(x: Profile) = PlanFacet.Profile x

        member inline _.Yield(x: Plan) = PlanFacet.Custom x

        member inline _.Combine(x1: PlanFacet, x2: PlanFacet list) =

            x1 :: x2


        member inline _.Delay([<InlineIfLambda>] a: unit -> unit) = []
        member inline _.Delay([<InlineIfLambda>] a: unit -> PlanFacet list) = a () // normal delay method for

        member inline _.Delay([<InlineIfLambda>] a: unit -> PlanFacet) = [ a () ]
        //member inline _.Run(state: PlanFacet) = [state]
        member inline _.Run(state: PlanFacet list) : Plan =

            let basePlan =
                { range =
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

            match customPlan with
            | Some c ->
                { basePlan with
                    range = c.range |> Option.orElse basePlan.range
                    profiles = c.profiles }
            | None -> basePlan

    let plan = PlanBuilder()
    let inline profile id = ProfileBuilder id
    let range = RangeBuilder()




    [<RequireQualifiedAccess>]
    module Plan =

        let env =
            Lazy<DbtEnv>(fun () ->
                let env: DbtEnv = Env.get<DbtEnv> ()

                { env with
                    DBT_BASE_COMMIT =
                        env.DBT_BASE_COMMIT
                        |> Option.orElseWith (fun () -> LastSuccessSha.getLastSuccessCommitHash () |> _.toOption) })

        let define (custom: Plan) =
            let env = env.Value

            plan {
                range {
                    from_ref env.DBT_BASE_COMMIT
                    to_ref env.DBT_CURRENT_COMMIT
                }

                custom
            }

        let create (plan: Plan) : PipelineOutput =

            let env = env.Value
            Log.trace "DBT Build Plan"
            Log.trace $"Mode: %s{env.DBT_MODE.ToString().ToLower()}"
            Log.trace $"Target: %s{env.DBT_PROFILE}"

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
