namespace Arquidev.Dbt

type ProjectMetadata =
    { fileName: string
      fullPath: string
      fullDir: string
      dir: string
      relativePath: string
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
      pattern: string
      patternIgnores: string list
      isRequired: string -> bool
      isIgnored: string -> bool
      projectId: ProjectMetadata -> string
      expandLeafs: Selector -> string -> string seq }

    static member internal Default =
        { id = "none"
          pattern = "none"
          patternIgnores = []
          isIgnored = fun _ -> false
          isRequired = fun _ -> true
          projectId = fun p -> p.projectId
          expandLeafs = fun _ path -> Seq.singleton path }

type ChangeSetRange =
    { baseCommits: string list
      currentCommit: string }

type PlanOutput =
    { requiredProjects: ProjectMetadata list
      changeKeys: string list option
      changeSetRange: ChangeSetRange option }

type DiffResult =
    { effectiveRange: ChangeSetRange
      dirs: string seq }

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
    { environment: string
      new_tag: string
      version: int64
      change_keys: string list option
      images: ImageSpec list }

and ImageSpec = { name: string; digest: string option }
