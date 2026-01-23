#load "../fsx/git-diff.fsx"
#load "../fsx/dotnet/project.fsx"
#load "../fsx/dotnet/solution.fsx"
#load "../fsx/node/project.fsx"
#load "../fsx/utils.fsx"
#load "../fsx/plan.fsx"
#load "../fsx/types.fsx"

open Arquidev.Dbt

GitDiff.dirsFromDiff false (Some "HEAD^^^") (Some "HEAD")
