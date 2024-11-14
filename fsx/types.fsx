namespace Arquidev.Dbt

open System.IO

type Selector =
    { kind: string
      pattern: string
      isRequired: string -> bool
      isIgnored: string -> bool
      safeName: string -> string
      expandLeafs: Selector -> string -> string seq }

    static member internal Default =
        { kind = "none"
          pattern = "none"
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
      images: ImageSpec list }

and ImageSpec = { name: string; digest: string option }
