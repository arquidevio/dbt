# dbt
## dependable build tools

F# toolkit for monorepo builds: given a git change set, find the projects to build.

## Quickstart

`plan.fsx`:

```fsharp
#r "paket: github arquidevio/dbt"
#load "arquidevio/dbt/fsx/plan.fsx"
#load "arquidevio/dbt/fsx/dotnet/project.fsx"

open Arquidev.Dbt

plan {
  profile {
    selector.defaults.dotnet.image
    post_action (fun o -> for p in o.requiredProjects do printfn $"{p.projectId} {p.relativePath}")
  }
}
|> Plan.evaluate
|> ignore
```

```bash
fsy run plan.fsx                # changes since HEAD's parent(s)
DBT_MODE=all fsy run plan.fsx   # every project
```

Pin with `github arquidevio/dbt:<tag-or-commit>`. Requires [fsy](https://github.com/queil/fsy).

## How it works

1. Diff mode: changed files between the base and current commit, grouped by dir. All mode: every tracked file.
2. Each changed dir maps to its closest ancestor dir containing a file matching the selector `pattern`.
3. `expand_leafs` maps that project to the leaf projects to build (e.g. a library to the apps referencing it).
4. `required_when` / `ignored_when` / `exclude` filter the result.
5. Post actions run on the `PlanOutput`, then `Plan.evaluate` returns it. If nothing is required the process exits 0 before both, unless `run_post_actions_when_empty true`.

Run from the repo root.

## Plan

A plan holds profiles; `DBT_PROFILE` picks one (default: `default`). Each profile has one selector.

| profile op | |
|---|---|
| `id "x"` | profile id (default `default`) |
| `selector { ... }` | the selector |
| `include_root_dir true` | include root-level files and the root dir as a project dir (default `false`) |
| `post_action (fun output -> ...)` | run after evaluation; repeatable |
| `run_post_actions_when_empty true` | run post actions even if nothing is required |
| `change_key_regex "regex" "replacement"` | extract change keys from commit messages in range |
| `extend otherProfile` | inherit facets |

| selector op | |
|---|---|
| `id "x"` | becomes `ProjectMetadata.kind` |
| `pattern "*.fsproj"` | project file glob; repeatable |
| `exclude "dir/sub"` | exclusion glob, relative to cwd; repeatable |
| `discovery_root "dir"` | discovery root (default cwd) |
| `required_when (fun path -> ...)` | keep only matching projects (default all) |
| `ignored_when (fun path -> ...)` | drop matching projects silently |
| `project_id (fun meta -> ...)` | override project id (default from the relative dir) |
| `expand_leafs (fun ctx -> ...)` | project to leaf projects (default identity) |
| `extend selector.defaults.x.y` | customize a built-in selector |

`range { from_ref "HEAD~3"; to_ref "HEAD" }` in a plan overrides `DBT_BASE_COMMIT`/`DBT_CURRENT_COMMIT`.

## Built-in selectors

| selector | load | selects |
|---|---|---|
| `selector.defaults.dotnet.generic` | `dotnet/project.fsx` | `*.*sproj`, expanded to leaf dependants via the `.sln`/`.slnx` in cwd; test projects ignored |
| `selector.defaults.dotnet.image` | | `generic` with `IsPublishable=true` |
| `selector.defaults.dotnet.nuget` | | `generic` with `IsPackable=true`, all packable dependants |
| `selector.defaults.node.image` | `node/project.fsx` | `package.json` |
| `selector.defaults.bicep.generic` | `bicep/project.fsx` | `.bicepparam` dependants of changed `.bicep` (`module`, `using`, `import`) |
| `selector.defaults.kustomize.generic` | `kustomize/project.fsx` | leaf `kustomization.yaml` dirs via `resources`/`components` |
| `selector.defaults.markdown.agents` | `markdown/project.fsx` | `AGENTS.md` whose referenced files changed |
| `selector.defaults.markdown.generic` | | same, bring your own `pattern` |

Customize:

```fsharp
selector {
  discovery_root "infra"
  required_when (fun p -> p.Contains "/envs/")
  extend selector.defaults.kustomize.generic
}
```

## Environment

| var | default | |
|---|---|---|
| `DBT_MODE` | `diff` | `diff` or `all` |
| `DBT_PROFILE` | `default` | profile to evaluate |
| `DBT_BASE_COMMIT` | | base commit override |
| `DBT_CURRENT_COMMIT` | `HEAD` | current commit |
| `DBT_PR_TARGET_BRANCH` | | base = merge-base with `origin/<branch>` (fetched) |
| `DBT_CI` | | `GITHUB` or `TEKTON`: base = last successful run's commit |
| `DBT_LOG_LEVEL` | `info` | |
| `DBT_SNAPSHOT` | | `write` or `validate`, see [Snapshots](#snapshots) |
| `DBT_SNAPSHOT_DIR` | cwd | |

Base commit: `DBT_PR_TARGET_BRANCH` > `range`/`DBT_BASE_COMMIT` > `DBT_CI` last success > parent(s) of current.

Don't use a shallow clone: diffs against missing history and change keys come out incomplete.

## CI

Load a module and pipe the plan output through it.

| module | functions |
|---|---|
| `ci/github/output.fsx` | `Plan.appendToGithubOutput key value`, `...With key f`, `...Json key f` (writes `$GITHUB_OUTPUT`) |
| `ci/github/deploy-spec.fsx` | `Plan.deploymentSpec output` from `GITHUB_*` vars |
| `ci/tekton/output.fsx` | `Plan.writeToTektonResult name value`, `...With`, `...Object`, `...Array` (writes `$RESULT_<name>`) |
| `ci/tekton/deploy-spec.fsx` | `Plan.deploymentSpec output` from `DBT_SOURCE_REPO`, `DBT_SOURCE_BRANCH`, `DBT_REVISION`, `DBT_RUN_VERSION` |

Last-success lookup (`DBT_CI`) needs `GITHUB_TOKEN` on GitHub; `TEKTON_RESULTS_HOST`, `RESULTS_PARENT`, `PIPELINE_NAME`, `DBT_SOURCE_BRANCH` on Tekton.

```fsharp
plan { ... }
|> Plan.evaluate
|> Plan.appendToGithubOutputJson "projects" (fun o -> o.requiredProjects |> List.map _.projectId)
|> ignore
```

## Markdown docs

Keeps docs such as `AGENTS.md` in sync with the code they describe.

```fsharp
#load "arquidevio/dbt/fsx/markdown/project.fsx"

let docs = selector { extend selector.defaults.markdown.agents }

plan {
  profile {
    id "docs"
    include_root_dir true
    run_post_actions_when_empty true
    docs
    post_action (MarkdownDoc.check docs)
  }
}
|> Plan.evaluate
```

- References: relative links (`[x](path)`, against the doc's dir, `/` = repo root) and path-like inline code (`` `src/app.fsx` ``, against the repo root). Code blocks, anchors, URLs and template tokens (`<x>`, globs, `../`, `~`) are ignored.
- A doc is selected when a referenced file, or a file under a referenced dir, changed.
- `MarkdownDoc.check` errors (exit 1) on missing references in any matching doc, and warns when a selected doc itself didn't change (diff mode only).
- Limits: a doc only tracks changes under its own dir, nested docs shadow parent docs, a root doc needs `include_root_dir true`.

## Snapshots

Regression-test a plan against known commit ranges:

```bash
DBT_SNAPSHOT=write    DBT_PROFILE=p DBT_BASE_COMMIT=a DBT_CURRENT_COMMIT=b fsy run plan.fsx
DBT_SNAPSHOT=validate DBT_PROFILE=p DBT_BASE_COMMIT=a DBT_CURRENT_COMMIT=b fsy run plan.fsx
```

Writes `.dbt-<profile>-<base>-<cur>.snapshot.json`. Validate exits 1 on mismatch and writes `.snapshot-miss.json`. See [arquidevio/test-dbt](https://github.com/arquidevio/test-dbt).

## Interactive

```bash
dnx fsy install-fsx-extensions   # dotnet 10+; older: dotnet tool install -g fsy && fsy install-fsx-extensions
dotnet fsi --compilertool:$(echo ~/.fsharp/fsx-extensions/.fsch) --use:./.fsi/plan.fsx
```

```fsharp
plan {
  range { from_ref "HEAD^^^"; to_ref "HEAD" }
  profile { selector { pattern "*.fsx" } }
} |> Plan.evaluate;;
```
