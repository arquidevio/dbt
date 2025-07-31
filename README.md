# dbt
## dependable build tools

An F# toolkit for monorepo builds.


## Interactive usage

Authoring a `plan` can be done in an interactive manner using `dotnet fsi`.

Prerequisites

* A `paket` dependency manager provided to `dotnet fsi` via `--compilertool`. It can be installed via the [fsy](https://github.com/queil/fsy) dotnet tool.

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
