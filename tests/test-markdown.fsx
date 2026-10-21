#r "paket: nuget Expecto ~> 10"

#load "../fsx/plan.fsx"
#load "../fsx/markdown/project.fsx"

open Expecto
open Arquidev.Dbt
open System.Diagnostics
open System.IO

let write (root: string) (path: string) (content: string) =
  let full = Path.Combine(root, path)
  Directory.CreateDirectory(Path.GetDirectoryName full) |> ignore
  File.WriteAllText(full, content)

let withDir (f: string -> unit) =
  let root =
    Path.Combine(Directory.GetCurrentDirectory(), $"tmp-dbt-test-{System.Guid.NewGuid():N}")

  Directory.CreateDirectory root |> ignore

  try
    f root
  finally
    Directory.Delete(root, true)

let agentsDoc =
  """# Agents

Entry point: `src/app.fsx`. Config lives in `config/`.
See the [guide](docs/guide.md#setup) and [home](https://example.com).

Placeholders: `<app>/main.fsx`, `src/*.fsx`, `../outside.md`, `dotnet test`, `~/.config/x.json`.

```bash
cat `nowhere/missing.fsx`
```

[ref-style][r]

[r]: docs/ref.md
"""

let withRepo (f: string -> unit) =
  withDir (fun root ->
    let w = write root
    w "AGENTS.md" agentsDoc
    w "src/app.fsx" ""
    w "src/lib/util.fsx" ""
    w "config/settings.json" ""
    w "docs/guide.md" ""
    w "docs/ref.md" ""
    w "other/x.txt" ""
    f root)

let mdSelector (root: string) =
  selector {
    discovery_root root
    extend selector.defaults.markdown.agents
  }
  |> makeSelector

let changes (files: string list) =
  files |> List.groupBy Path.GetDirectoryName |> Map.ofList

let select (root: string) (includeRootDir: bool) (files: string list) =
  findRequiredProjects
    {
      filesByDir = files |> List.map (fun f -> Path.Combine(root, f)) |> changes
    }
    includeRootDir
    (mdSelector root)
  |> Seq.toList

let selectedPaths root includeRootDir files =
  select root includeRootDir files |> List.map _.fullPath

let analyzeDiff (root: string) (files: string list) =
  let required = select root true files

  MarkdownDoc.analyze
    (mdSelector root)
    {
      requiredProjects = required
      changeKeys = None
      changeSetRange = None
      changedDirs = Some(files |> List.map (fun f -> Path.Combine(root, f)) |> changes)
    }

let pathLike =
  test "Path-like inline code tokens" {
    for token in [ "src/app.fsx"; "config/"; "Dockerfile"; "package.json"; "./x/y" ] do
      $"%s{token} should be a path" |> Expect.isTrue (MarkdownDoc.isPathLike token)

    for token in
      [
        "<app>/main.fsx"
        "src/*.fsx"
        "../outside.md"
        "/etc/hosts"
        "-v"
        "~/.config/x.json"
        "dotnet test"
        "https://x.io/a.md"
        "${X}/a.md"
        "README"
        "v7.0.2"
      ] do
      $"%s{token} should not be a path"
      |> Expect.isFalse (MarkdownDoc.isPathLike token)
  }

let referencesParsed =
  test "References: links, path-like code, no code blocks or templates" {
    withRepo (fun root ->
      let refs =
        MarkdownDoc.references root (Path.Combine(root, "AGENTS.md"))
        |> List.map (fun r -> Path.GetRelativePath(root, r.fullPath))
        |> List.sort

      "Only real references"
      |> Expect.sequenceEqual refs [ "config"; "docs/guide.md"; "docs/ref.md"; "src/app.fsx" ])
  }

let linksRelativeToDoc =
  test "Links resolve relative to the doc dir, code relative to root" {
    withDir (fun root ->
      write root "sub/AGENTS.md" "[a](a.md) [b](/b.md) `c.md`"

      let refs =
        MarkdownDoc.references root (Path.Combine(root, "sub/AGENTS.md"))
        |> List.map (fun r -> Path.GetRelativePath(root, r.fullPath))
        |> List.sort

      "Resolved paths" |> Expect.sequenceEqual refs [ "b.md"; "c.md"; "sub/a.md" ])
  }

let unreferencedChange =
  test "Unreferenced file changed: nothing selected, no warning" {
    withRepo (fun root ->
      "Nothing selected" |> Expect.isEmpty (selectedPaths root true [ "other/x.txt" ])
      "No warning" |> Expect.isEmpty (analyzeDiff root [ "other/x.txt" ]).stale)
  }

let referencedChange =
  test "Referenced file changed: doc selected, warning" {
    withRepo (fun root ->
      "Doc selected"
      |> Expect.equal (selectedPaths root true [ "src/app.fsx" ]) [ Path.Combine(root, "AGENTS.md") ]

      "Warning lists the changed file"
      |> Expect.equal
        (analyzeDiff root [ "src/app.fsx" ]).stale
        [ Path.Combine(root, "AGENTS.md"), [ Path.Combine(root, "src/app.fsx") ] ])
  }

let referencedAndDocChange =
  test "Referenced file and doc changed: no warning" {
    withRepo (fun root ->
      "No warning"
      |> Expect.isEmpty (analyzeDiff root [ "src/app.fsx"; "AGENTS.md" ]).stale)
  }

let underReferencedDir =
  test "File under a referenced dir changed: warning" {
    withRepo (fun root ->
      "Warning"
      |> Expect.equal
        (analyzeDiff root [ "config/settings.json" ]).stale
        [
          Path.Combine(root, "AGENTS.md"), [ Path.Combine(root, "config/settings.json") ]
        ])
  }

let missingReferences =
  test "Missing path and broken link: errors" {
    withRepo (fun root ->
      write root "AGENTS.md" "`src/gone.fsx` [x](docs/nope.md) `src/app.fsx`"

      "Both missing references reported"
      |> Expect.sequenceEqual
        ((analyzeDiff root [ "other/x.txt" ]).missing
         |> List.map (fun r -> r.kind, r.raw))
        [ Link, "docs/nope.md"; Code, "src/gone.fsx" ])
  }

let allMode =
  test "All mode: existence checks only" {
    withRepo (fun root ->
      write root "AGENTS.md" "`src/app.fsx` [x](docs/nope.md)"

      let result =
        MarkdownDoc.analyze
          (mdSelector root)
          {
            requiredProjects = select root true [ "src/app.fsx"; "AGENTS.md" ]
            changeKeys = None
            changeSetRange = None
            changedDirs = None
          }

      "No warnings" |> Expect.isEmpty result.stale

      "Missing reported"
      |> Expect.equal (result.missing |> List.map _.raw) [ "docs/nope.md" ])
  }

let rootDoc =
  test "Root doc selected only with include_root_dir" {
    withRepo (fun root ->
      "Selected with include_root_dir"
      |> Expect.equal (selectedPaths root true [ "src/app.fsx" ]) [ Path.Combine(root, "AGENTS.md") ]

      "Not selected without"
      |> Expect.isEmpty (selectedPaths root false [ "src/app.fsx" ]))
  }

let nestedDocShadows =
  test "Nested doc shadows the root doc (documented limitation)" {
    withRepo (fun root ->
      write root "src/AGENTS.md" "`src/lib/`"

      "Root doc not selected for changes under src"
      |> Expect.isEmpty (selectedPaths root true [ "src/app.fsx" ])

      "Nested doc selected"
      |> Expect.equal (selectedPaths root true [ "src/lib/util.fsx" ]) [ Path.Combine(root, "src/AGENTS.md") ])
  }

let uniqueProjectIds =
  test "Project ids derive from the doc path" {
    withRepo (fun root ->
      write root "src/AGENTS.md" "`src/lib/`"

      let ids =
        [
          select root true [ "src/lib/util.fsx" ]
          select root true [ "config/settings.json" ]
        ]
        |> List.concat
        |> List.map _.projectId

      // relative to cwd, as for all selectors
      let prefix =
        Path
          .GetRelativePath(Directory.GetCurrentDirectory(), root)
          .ToLowerInvariant()
          .Replace('_', '-')
          .Replace('.', '-')

      "Ids"
      |> Expect.sequenceEqual ids [ $"{prefix}-src-agents-md"; $"{prefix}-agents-md" ])
  }

let customPattern =
  test "Generic selector with a custom pattern" {
    let s =
      plan {
        profile {
          selector {
            pattern "CLAUDE.md"
            extend selector.defaults.markdown.generic
          }
        }
      }
      |> _.profiles.Value["default"].selector.Value

    "Only the custom pattern" |> Expect.sequenceEqual s.patterns [ "CLAUDE.md" ]
    "Markdown id" |> Expect.equal s.id "markdown"
  }

let postActionsWhenEmpty =
  test "run_post_actions_when_empty defaults to false" {
    let p (facets: ProfileFacet list) =
      (plan { facets }).profiles.Value["default"].runPostActionsWhenEmpty

    "Default" |> Expect.isFalse (p (profile { selector { pattern "x" } }))

    "Set"
    |> Expect.isTrue (
      p (
        profile {
          run_post_actions_when_empty true
          selector { pattern "x" }
        }
      )
    )
  }

// End-to-end: Plan.evaluate in a throwaway git repo

let dbtFsx = Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "../fsx"))

let planScript =
  $"""#load "%s{dbtFsx}/plan.fsx"
#load "%s{dbtFsx}/markdown/project.fsx"

open Arquidev.Dbt

let docs = selector {{ extend selector.defaults.markdown.agents }}

plan {{
  profile {{
    id "docs"
    include_root_dir true
    run_post_actions_when_empty true
    docs
    post_action (MarkdownDoc.check docs)
  }}
  profile {{
    id "docs-no-empty"
    include_root_dir true
    docs
    post_action (MarkdownDoc.check docs)
  }}
}}
|> Plan.evaluate
"""

let run (cwd: string) (env: (string * string) list) (cmd: string) (args: string list) =
  let psi =
    ProcessStartInfo(cmd, args, WorkingDirectory = cwd, RedirectStandardOutput = true, RedirectStandardError = true)

  for k, v in env do
    psi.Environment[k] <- v

  use p = Process.Start psi
  let out = p.StandardOutput.ReadToEndAsync()
  let err = p.StandardError.ReadToEndAsync()
  p.WaitForExit()
  p.ExitCode, out.Result + err.Result

let git root args =
  match run root [] "git" args with
  | 0, out -> out.Trim()
  | code, out -> failwith $"git %A{args} failed (%d{code}): %s{out}"

let commit root (files: (string * string) list) =
  for path, content in files do
    write root path content

  git root [ "add"; "-A" ] |> ignore

  git root [ "-c"; "user.name=t"; "-c"; "user.email=t@t"; "commit"; "-qm"; "c" ]
  |> ignore

  git root [ "rev-parse"; "HEAD" ]

let e2e =
  testSequenced
  <| test "End-to-end: Plan.evaluate exit codes and output" {
    withDir (fun root ->
      git root [ "init"; "-q" ] |> ignore
      write root "plan.fsx" planScript

      let a =
        commit
          root
          [
            "AGENTS.md", "`src/app.fsx` [guide](docs/guide.md)"
            "src/app.fsx", ""
            "docs/guide.md", ""
            "other/x.txt", ""
          ]

      let b = commit root [ "src/app.fsx", "changed" ]
      let c = commit root [ "other/x.txt", "changed" ]

      let d =
        commit root [ "AGENTS.md", "`src/app.fsx` [guide](docs/guide.md) [gone](docs/gone.md)" ]

      let evaluate mode profile range =
        let env =
          [ "DBT_MODE", mode; "DBT_PROFILE", profile ]
          @ (match range with
             | Some(from, ``to``) -> [ "DBT_BASE_COMMIT", from; "DBT_CURRENT_COMMIT", ``to`` ]
             | None -> [])

        git root [ "checkout"; "-q"; (range |> Option.map snd |> Option.defaultValue d) ]
        |> ignore

        // the repo is under the dbt checkout so the local fsy tool resolves, as in CI
        run root env "dotnet" [ "fsy"; "run"; "plan.fsx" ]

      let expect name (code, out: string) expectedCode (contains: string list) (notContains: string list) =
        $"%s{name}: exit code\n%s{out}" |> Expect.equal code expectedCode

        for s in contains do
          $"%s{name}: output contains '%s{s}'" |> Expect.stringContains out s

        for s in notContains do
          $"%s{name}: output does not contain '%s{s}'" |> Expect.isFalse (out.Contains s)

      expect "referenced changed" (evaluate "diff" "docs" (Some(a, b))) 0 [ "may be stale" ] [ "does not exist" ]

      expect
        "unrelated changed, opt-in"
        (evaluate "diff" "docs" (Some(b, c)))
        0
        [ "MARKDOWN CHECK" ]
        [ "may be stale" ]

      expect "unrelated changed, no opt-in" (evaluate "diff" "docs-no-empty" (Some(b, c))) 0 [] [ "MARKDOWN CHECK" ]

      expect "broken link added, opt-in" (evaluate "diff" "docs" (Some(c, d))) 1 [ "'docs/gone.md' does not exist" ] []

      expect "broken link added, no opt-in" (evaluate "diff" "docs-no-empty" (Some(c, d))) 0 [] [ "MARKDOWN CHECK" ]

      expect "all mode, broken" (evaluate "all" "docs" None) 1 [ "'docs/gone.md' does not exist" ] [ "may be stale" ]

      expect "all mode, valid" (evaluate "all" "docs" (Some(c, c))) 0 [ "MARKDOWN CHECK" ] [ "may be stale" ])
  }

[<Tests>]
let tests =
  [
    pathLike
    referencesParsed
    linksRelativeToDoc
    unreferencedChange
    referencedChange
    referencedAndDocChange
    underReferencedDir
    missingReferences
    allMode
    rootDoc
    nestedDocShadows
    uniqueProjectIds
    customPattern
    postActionsWhenEmpty
    e2e
  ]
  |> testList "Markdown"

runTestsWithCLIArgs [] [||] tests
