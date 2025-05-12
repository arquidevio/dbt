namespace Arquidev.Dbt

open System.IO

type Selector =
    { id: string
      pattern: string
      patternIgnores: string list
      isRequired: string -> bool
      isIgnored: string -> bool
      safeName: string -> string
      expandLeafs: Selector -> string -> string seq }

    static member internal Default =
        { id = "none"
          pattern = "none"
          patternIgnores = []
          isIgnored = fun _ -> false
          isRequired = fun _ -> true
          safeName =
            fun path ->
                path
                |> Path.GetDirectoryName
                |> Path.GetFileName
                |> fun p -> p.ToLowerInvariant().Replace(".", "-")
          expandLeafs = fun _ path -> Seq.singleton path }

type ProjectPath =
    { path: string
      dir: string
      safeName: string
      kind: string }

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
