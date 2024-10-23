namespace Arquidev.Dbt

open System.IO

type ProjectPath =
    { path: string
      safeName: string
      kind: string }

type Selector =
    { kind: string
      pattern: string
      isRequired: string -> bool
      isIgnored: string -> bool
      safeName: string -> string
      expandLeafs: string -> string seq }

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
          expandLeafs = Seq.singleton }
