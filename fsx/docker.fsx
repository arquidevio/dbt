#r "paket:
  nuget Fake.Tools.Git >= 6.0.0
  nuget Fake.Core.Environment >= 6.0.0
  nuget Fake.Core.Trace >= 6.0.0"

open Fake.Tools.Git
open System
open System.IO

open Fake.Core

let pwd = Directory.GetCurrentDirectory()

let generateRestoreList () =

    let input = StreamRef.Empty

    let tar =
        CreateProcess.fromRawCommand
            "tar"
            [ "--sort=name"
              "--owner=root:0"
              "--group=root:0"
              "--mtime=2023-01-01 00:00:00"
              "-czvf"
              "restore-list.tar.gz"
              "-T"
              "-" ]
        |> CreateProcess.withStandardInput (CreatePipe input)
        |> Proc.start

    let restoreList =
        CreateProcess.fromRawCommand "dotnet" [ "sln"; "list" ]
        |> CreateProcess.redirectOutput
        |> Proc.run
        |> fun f -> f.Result.Output.Split(Environment.NewLine) |> Seq.ofArray
        |> Seq.skip (2)
        |> Seq.map (fun path -> path.Replace(pwd, String.Empty).Replace("\\", "/"))
        |> Seq.iter (fun path -> input.Value.Write(Text.Encoding.UTF8.GetBytes(path + Environment.NewLine)))

    input.Value.Flush()
    input.Value.Close()

    tar.Wait()
