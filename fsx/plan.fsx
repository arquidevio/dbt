namespace Arquidev.Dbt

#load "plan.builder.fsx"
#load "git-diff.fsx"
#load "tools/git.fsx"
#load "ci/github/last-success-sha.fsx"
#load "ci/tekton/last-success-sha.fsx"
#load "snapshot.fsx"

#r "paket:
        nuget Microsoft.Extensions.FileSystemGlobbing ~> 10
        nuget Arquidev.Env ~> 2.1.0
        nuget Arquidev.Log.Console ~> 0.1.0
"

open Arquidev.Tools
open Arquidev.Dbt
open Microsoft.Extensions.FileSystemGlobbing

[<AutoOpen>]
module Pipeline =
  open System.IO

  let private log = Log.Source "Arquidev.Dbt.Pipeline"

  [<Literal>]
  let private NoProject = "(none)"

  /// Find the closest ancestor dir of the originPath that contains files matching the project pattern.
  /// Returns all matches found in the first directory that has any.
  let findParentProjectPath
    (rootDir: string)
    (projectMatcher: Matcher)
    (projectExcludeMatcher: Matcher)
    (includeRootDir: bool)
    (originPath: string)
    : string list =

    let rec findParentProj (path: string) =

      match
        projectMatcher.GetResultsInFullPath path
        |> projectExcludeMatcher.Match
        |> _.Files
        |> Seq.map _.Path
        |> Seq.toList
      with
      | [] ->
        match Directory.GetParent path with
        | null -> []
        | p when not includeRootDir && p.FullName = rootDir -> []
        | p -> findParentProj p.FullName
      | matches -> matches |> List.map Path.GetFullPath

    findParentProj originPath

  let findRequiredProjects (ctx: SelectionContext) (includeRootDir: bool) (selector: Selector) =

    let discoveryRoot =
      let cwd = Directory.GetCurrentDirectory()

      selector.discoveryRoot
      |> Option.map (fun path -> Path.Combine(cwd, path))
      |> Option.defaultValue cwd

    log.debug "discovery root: %s" discoveryRoot
    log.debug "patterns: %A" selector.patterns
    log.debug "exclude: %A" selector.excludePatterns

    let projectMatcher = Matcher()
    selector.patterns |> List.iter (fun p -> projectMatcher.AddInclude p |> ignore)
    let projectExcludeMatcher = Matcher()
    projectExcludeMatcher.AddInclude("**/*.*").AddExcludePatterns selector.excludePatterns

    let findParentProjects =

      let findParent =
        findParentProjectPath discoveryRoot projectMatcher projectExcludeMatcher includeRootDir

      if log.isEnabled Log.Level.debug then
        fun dirs ->
          let groups = dirs |> Seq.groupBy findParent |> Seq.toList

          for projects, changedDirs in groups do
            match projects with
            | [] -> log.debug "Project: '%s'" NoProject
            | ps -> ps |> List.iter (log.debug "Project: '%s'")

            changedDirs |> Seq.iter (log.debug " - %s")

          groups |> Seq.collect fst
      else
        Seq.collect findParent

    ctx.filesByDir
    |> Map.keys
    |> findParentProjects
    |> Seq.distinct
    |> Seq.collect (fun projectPath ->
      selector.expandLeafs
        { selector = selector
          projectPath = projectPath
          filesByDir = ctx.filesByDir })
    |> Seq.filter (fun p -> (projectExcludeMatcher.Match p).HasMatches)
    |> Seq.distinct
    |> Seq.filter (not << selector.isIgnored)
    |> Seq.toList
    |> fun paths ->
        let neitherIgnoredNorRequired =
          paths |> Seq.except (paths |> Seq.filter selector.isRequired)

        for path in neitherIgnoredNorRequired do
          log.warn
            $"WARNING: %s{path} is a leaf project not matching the inclusion criteria. The project will be ignored."

        paths
    |> Seq.filter selector.isRequired
    |> Seq.map (fun p ->
      let file = FileInfo p
      let cwd = Directory.GetCurrentDirectory()
      let relativeDir = Path.GetRelativePath(cwd, file.DirectoryName)

      let output =
        { kind = selector.id
          fileName = file.Name
          fileNameNoExtension = Path.GetFileNameWithoutExtension file.Name
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
          projectId = selector.projectId output })
    |> Seq.sortBy (fun p -> p.projectId)

  type Mode =
    | All
    | Diff

  type DbtEnv =
    { [<Env.Default("diff")>]
      DBT_MODE: Mode
      [<Env.Default("default")>]
      DBT_PROFILE: string
      DBT_CURRENT_COMMIT: string option
      DBT_BASE_COMMIT: string option
      DBT_LOG_LEVEL: Log.Level option
      DBT_PR_TARGET_BRANCH: string option
      DBT_CI: string option }

  [<RequireQualifiedAccess>]
  module Plan =

    let env =
      Lazy<DbtEnv>(fun () ->
        let env: DbtEnv = readEnv<DbtEnv> ()

        { env with
            DBT_BASE_COMMIT =
              env.DBT_BASE_COMMIT
              |> Option.orElseWith (fun () ->
                match env.DBT_PR_TARGET_BRANCH with
                | Some _ -> None
                | _ ->
                  match env.DBT_CI with
                  | Some "TEKTON" -> Tekton.LastSuccessSha.getLastSuccessCommitHash () |> _.toOption
                  | Some "GITHUB" -> Github.LastSuccessSha.getLastSuccessCommitHash () |> _.toOption
                  | Some ci -> failwith $"Last success commit hash for: %s{ci} not supported"
                  | None -> None) })

    let evaluate (plan: Plan) : PlanOutput =
      let env = env.Value

      use _ =
        Log.Console.enableWithLevel (env.DBT_LOG_LEVEL |> Option.defaultValue Log.Level.info)

      log.info "DBT Build Plan"

      let plan =
        match plan.range with
        | None ->
          { plan with
              range =
                Some
                  { fromRef = env.DBT_BASE_COMMIT
                    toRef = env.DBT_CURRENT_COMMIT } }
        | Some _ -> plan

      log.trace "%A" plan
      log.info $"Mode: %s{env.DBT_MODE.ToString().ToLower()}"
      log.info $"Profile: %s{env.DBT_PROFILE}"

      let mode = env.DBT_MODE
      let profileId = env.DBT_PROFILE

      let profile =
        match plan.profiles with
        | Some p when p |> Map.count > 0 && p.ContainsKey profileId -> p[profileId]
        | _ -> failwithf $"Profile {profileId} not configured"

      let dirs, diffRange =
        match mode with
        | Diff ->
          log.header "GIT CHANGE SET"

          let baseCommitStrategy (fromRef: string option) =
            match env.DBT_PR_TARGET_BRANCH with
            | Some branch -> MergeBase branch
            | None ->
              match fromRef with
              | Some ref -> Override ref
              | None -> Parent

          let result =
            plan.range
            |> Option.map (fun r -> profile.includeRootDir, baseCommitStrategy r.fromRef, r.toRef)
            |> Option.defaultValue ((profile.includeRootDir, baseCommitStrategy None, None))
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
        { requiredProjects =
            findRequiredProjects { filesByDir = dirs } profile.includeRootDir selector
            |> Seq.toList
          changeSetRange = diffRange
          changedDirs =
            match mode with
            | Diff -> Some dirs
            | All -> None
          changeKeys =
            diffRange
            |> Option.map (fun r ->
              profile.changeKeyPrefixRegex
              |> Option.map (fun (regex, _) ->
                r.baseCommits
                |> Seq.collect (fun baseCommit -> Git.Repo(".").ParseCommitMessage baseCommit r.currentCommit regex)
                |> Seq.distinct
                |> Seq.toList))
            |> Option.flatten }

      log.trace "%A" result

      Snapshot.apply result

      if result.requiredProjects.Length = 0 then
        log.info "No project changes. Exiting"
#if !INTERACTIVE
        exit 0
#endif
      for action in profile.postActions do
        action result

      result
