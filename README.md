# dbt
## dependable build tools

An F# toolkit for monorepo builds.

## Warnings

* if using the change keys extraction feature make sure the repo is not a shallow clone. Otherwise basing on an incomplete git log will result in missing change keys

## Interactive usage

Authoring a `plan` can be done in an interactive manner using `dotnet fsi`.

Prerequisites

* A `paket` dependency manager provided to `dotnet fsi` via `--compilertool`.

It can be installed via the [fsy](https://github.com/queil/fsy) dotnet tool.
  * in dotnet 10 onwards it can be installed via `dnx fsy install-fsx-extensions`
  * dotnet < 10:
      1. Install `fsy`: `dotnet tool install --global fsy`
      2. Install the `paket` manager files via: `fsy install-fsx-extensions` (it copies it to: `~/.fsharp/fsx-extensions/.fsch`)

Pre-load an fsi session with `fsx/plan.fsx`:

```bash
dotnet fsi --compilertool:$(echo ~/.fsharp/fsx-extensions/.fsch) --use:./.fsi/plan.fsx
```

Create and evaluate a plan:

```fsharp
plan {
    range {
        from_ref "HEAD^^^"
        to_ref "HEAD" 
    }
    profile { 
        selector { pattern "*.fsx" }
    }
} |> Plan.evaluate;;
```

## Markdown selector

`fsx/markdown/project.fsx` treats markdown docs (e.g. `AGENTS.md`) as projects whose inputs are the files they reference. Use it to keep docs in sync with the code they describe.

References are:

* relative links and images: `[x](path)`, resolved against the doc's dir (or the repo root if starting with `/`). Anchors, queries and scheme URLs are ignored
* inline code that looks like a repo path: `` `src/app.fsx` ``, `` `config/` ``, resolved against the repo root (discovery root). A token counts if it has no whitespace or any of ``<>*{}$|=:'"` ``, doesn't start with `/`, `../`, `-` or `~`, and contains `/`, equals `Dockerfile` or has a known source extension (see `MarkdownDoc.sourceExtensions`)

Code blocks are not inspected.

Selectors:

* `selector.defaults.markdown.agents` - `AGENTS.md`
* `selector.defaults.markdown.generic` - no pattern, add your own (e.g. `pattern "CLAUDE.md"`)

A doc is selected when a changed file equals one of its references or lies under a referenced directory.

`MarkdownDoc.check` post action:

* errors (exit 1) for every doc matching the selector (selected or not) referencing a path that doesn't exist
* in `diff` mode warns for every selected doc which itself didn't change, listing the changed referenced files
* in `all` mode only checks existence

```fsharp
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

`run_post_actions_when_empty true` makes the existence check run even if no doc is selected (e.g. only a doc changed).

Limitations (the selector uses the standard closest-ancestor project discovery):

* a doc only tracks changes under its own directory
* a nested doc shadows parent docs for changes in its subtree
* a root doc requires `include_root_dir true`. Then it covers the whole repo (unless shadowed)
