namespace Arquidev.Dbt

type ProjectMetadata =
  { fileName: string
    fileNameNoExtension: string
    fullPath: string
    fullDir: string
    dir: string
    dirSlug: string
    relativePath: string
    relativeDir: string
    projectId: string
    kind: string }

  member x.IdTupleBy([<System.ParamArray>] c: char array) =
    let chunks = x.projectId.Split(c, 2)
    chunks[0], chunks[1]

  member x.IdTripleBy([<System.ParamArray>] c: char array) =
    let chunks = x.projectId.Split(c, 3)
    chunks[0], chunks[1], chunks[2]

type Selector =
  { id: string
    patterns: string list
    excludePatterns: string list
    discoveryRoot: string option
    isRequired: string -> bool
    isIgnored: string -> bool
    projectId: ProjectMetadata -> string
    expandLeafs: LeafExpansionContext -> string seq }

  static member internal Default =
    { id = "none"
      patterns = []
      excludePatterns = []
      discoveryRoot = None
      isIgnored = fun _ -> false
      isRequired = fun _ -> true
      projectId = fun p -> p.projectId
      expandLeafs = fun ctx -> Seq.singleton ctx.projectPath }

and LeafExpansionContext =
  { selector: Selector
    projectPath: string
    filesByDir: Map<string, string list> }

type ChangeSetRange =
  { baseCommits: string list
    currentCommit: string }

type PlanOutput =
  { requiredProjects: ProjectMetadata list
    changeKeys: string list option
    changeSetRange: ChangeSetRange option
    changedDirs: Map<string, string list> option }

type SelectionContext =
  { filesByDir: Map<string, string list> }

type DiffResult =
  { effectiveRange: ChangeSetRange
    dirs: Map<string, string list> }

type SnapshotMode =
  | Write
  | Validate

type BuildSpec = { docker: DockerBuildSpec list }

and DockerBuildSpec =
  { name: string
    file: string option
    context: string
    target: string option }

  static member Create name =
    { name = name
      file = None
      context = "."
      target = None }

type UpdateSpec =
  { source_repo: string
    source_branch: string
    new_tag: string
    version: int64
    change_keys: string list option
    images: ImageSpec list }

and ImageSpec = { name: string; digest: string option }
