#load "../fsx/env.fsx"
#load "../fsx/git.fsx"
#load "../fsx/dotnet/project.fsx"
#load "../fsx/dotnet/solution.fsx"
#load "../fsx/node/project.fsx"
#load "../fsx/utils.fsx"
#load "../fsx/pipeline.fsx"
#load "../fsx/types.fsx"

open Arquidev.Dbt

Git.dirsFromDiff (Env.get<GitDiffEnv> ())
