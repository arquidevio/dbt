#r "paket: nuget Markdig ~> 1.4
           nuget Microsoft.Extensions.FileSystemGlobbing ~> 10
           nuget Arquidev.Log ~> 0.3.0"

#load "../plan.builder.fsx"

namespace Arquidev.Dbt

open Arquidev.Tools
open Markdig
open Markdig.Syntax
open Markdig.Syntax.Inlines
open Microsoft.Extensions.FileSystemGlobbing
open System
open System.IO

type MarkdownReferenceKind =
  | Link
  | Code

type MarkdownReference =
  {
    doc: string
    raw: string
    fullPath: string
    kind: MarkdownReferenceKind
  }

type MarkdownCheckResult =
  {
    missing: MarkdownReference list
    stale: (string * string list) list
  }

[<RequireQualifiedAccess>]
module MarkdownDoc =

  let private log = Log.Source "Arquidev.Dbt.Markdown"

  let sourceExtensions =
    set
      [
        "md"
        "fsx"
        "yaml"
        "yml"
        "json"
        "ts"
        "js"
        "mjs"
        "sh"
        "py"
        "cs"
        "csproj"
        "slnx"
        "sln"
        "toml"
        "props"
        "bicep"
      ]

  let private forbiddenChars = set "<>*{}$|=:'\"`"

  /// Whether an inline code token looks like a repo-relative file or directory path
  let isPathLike (token: string) =
    let extension = Path.GetExtension(token).TrimStart '.'

    token <> ""
    && not (token |> Seq.exists (fun c -> Char.IsWhiteSpace c || forbiddenChars.Contains c))
    && not (
      token.StartsWith "/"
      || token.StartsWith "../"
      || token.StartsWith "-"
      || token.StartsWith "~"
    )
    && (token.Contains "/"
        || token = "Dockerfile"
        || sourceExtensions.Contains extension)

  let private normalize (path: string) =
    Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)

  let private hasScheme (url: string) =
    match url.IndexOf ':' with
    | -1 -> false
    | i ->
      i <
        (match url.IndexOf '/' with
         | -1 -> Int32.MaxValue
         | j -> j)

  let private linkTarget (url: string) =
    match url with
    | null -> None
    | url ->
      let target = url.Split([| '#'; '?' |], 2)[0]

      if target = "" || hasScheme target then
        None
      else
        Some(Uri.UnescapeDataString target)

  /// Extracts file references from a markdown doc:
  /// relative links/images (resolved against the doc's dir, or rootDir if starting with '/')
  /// and path-like inline code (resolved against rootDir).
  /// Code blocks are not inspected.
  let references (rootDir: string) (docPath: string) : MarkdownReference list =
    let docPath = Path.GetFullPath docPath
    let docDir = Path.GetDirectoryName docPath
    let md = Markdown.Parse(File.ReadAllText docPath)

    let links =
      md.Descendants<LinkInline>()
      |> Seq.choose (fun l ->
        linkTarget l.Url
        |> Option.map (fun target ->
          let path =
            if target.StartsWith "/" then
              Path.Combine(rootDir, target.TrimStart '/')
            else
              Path.Combine(docDir, target)

          {
            doc = docPath
            raw = l.Url
            fullPath = normalize path
            kind = Link
          }))

    let code =
      md.Descendants<CodeInline>()
      |> Seq.map _.Content
      |> Seq.filter isPathLike
      |> Seq.map (fun token ->
        {
          doc = docPath
          raw = token
          fullPath = normalize (Path.Combine(rootDir, token))
          kind = Code
        })

    Seq.append links code
    |> Seq.distinctBy (fun r -> r.fullPath, r.kind)
    |> Seq.toList

  /// Whether the file equals a referenced path or lies under a referenced directory
  let isReferenced (refs: MarkdownReference list) (filePath: string) =
    let file = normalize filePath

    refs
    |> List.exists (fun r ->
      file = r.fullPath
      || file.StartsWith(r.fullPath + string Path.DirectorySeparatorChar))

  let private changedFiles (filesByDir: Map<string, string list>) =
    filesByDir
    |> Map.values
    |> Seq.concat
    |> Seq.map normalize
    |> Seq.distinct
    |> Seq.toList

  let private rootDirOf (selector: Selector) =
    let cwd = Directory.GetCurrentDirectory()

    selector.discoveryRoot
    |> Option.map (fun p -> Path.Combine(cwd, p))
    |> Option.defaultValue cwd
    |> normalize

  /// Selects the doc only if one of its referenced files changed
  let expandLeafs (ctx: LeafExpansionContext) : string seq =
    let refs = references (rootDirOf ctx.selector) ctx.projectPath

    if changedFiles ctx.filesByDir |> List.exists (isReferenced refs) then
      Seq.singleton ctx.projectPath
    else
      Seq.empty

  /// All docs under the selector's discovery root matching its patterns (at any depth) minus excludes
  let findDocs (selector: Selector) : string list =
    let matcher = Matcher()
    selector.patterns |> List.iter (fun p -> matcher.AddInclude $"**/{p}" |> ignore)
    selector.excludePatterns |> List.iter (fun p -> matcher.AddExclude p |> ignore)
    matcher.GetResultsInFullPath(rootDirOf selector) |> Seq.sort |> Seq.toList

  /// Missing references across all docs and, in diff mode, selected docs that did not change
  /// together with the referenced files that did
  let analyze (selector: Selector) (output: PlanOutput) : MarkdownCheckResult =
    let rootDir = rootDirOf selector

    let missing =
      findDocs selector
      |> List.collect (references rootDir)
      |> List.filter (fun r -> not (Path.Exists r.fullPath))

    let stale =
      match output.changedDirs with
      | None -> []
      | Some dirs ->
        let changed = changedFiles dirs

        output.requiredProjects
        |> List.map (fun p -> normalize p.fullPath)
        |> List.filter (fun doc -> not (changed |> List.contains doc))
        |> List.map (fun doc ->
          let refs = references rootDir doc
          doc, changed |> List.filter (isReferenced refs))
        |> List.filter (snd >> List.isEmpty >> not)

    { missing = missing; stale = stale }

  let report (result: MarkdownCheckResult) =
    let cwd = Directory.GetCurrentDirectory()
    let rel (p: string) = Path.GetRelativePath(cwd, p)

    for doc, files in result.stale do
      log.warn
        $"""WARNING: %s{rel doc} may be stale. Referenced files changed: %s{files |> List.map rel |> String.concat ", "}"""

    for r in result.missing do
      log.error $"%s{rel r.doc}: %A{r.kind} reference '%s{r.raw}' does not exist"

  /// Post action: warns about stale docs and exits with 1 if any doc references a missing path
  let check (selector: SelectorFacet list) : PlanOutput -> unit =
    let selector = makeSelector selector

    fun output ->
      log.header "MARKDOWN CHECK"
      let result = analyze selector output
      report result

      if not result.missing.IsEmpty then
        exit 1


[<AutoOpen>]
module MarkdownSelectors =

  type SelectorBuilderDefaults with
    member _.markdown: Selectors = Selectors()

  and Selectors() =
    /// Markdown docs selected when any of their referenced files changed. Add a pattern to use it.
    member _.generic =
      selector {
        id "markdown"

        project_id (fun p ->
          p.relativePath.ToLowerInvariant().Split([| '/'; '.'; '_'; ' ' |])
          |> String.concat "-")

        expand_leafs MarkdownDoc.expandLeafs
      }

    /// AGENTS.md docs
    member x.agents =
      selector {
        pattern "AGENTS.md"
        extend x.generic
      }
