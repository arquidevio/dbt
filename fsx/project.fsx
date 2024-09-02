#r "paket: nuget Fake.Core.Trace = 6.1.1"

namespace Arquidev.Dbt

open Fake.Core
open System
open System.IO
open System.Xml.XPath

[<RequireQualifiedAccess>]
module Project =

    let safeName (projFullPath: string) : string =
        projFullPath
        |> Path.GetDirectoryName
        |> Path.GetFileName
        |> fun p -> p.ToLowerInvariant()
        |> fun p -> p.Split('.')
        |> fun p -> p[1..]
        |> fun p -> String.Join('-', p)
    //|> fun p -> printfn "%s" p; p // DEBUG

    let hasProperty (propertyName: string) (projPath: string) : bool =
        let xp = XPathDocument(projPath)
        let n = xp.CreateNavigator()

        n.SelectSingleNode($"/Project/PropertyGroup[*]/{propertyName}[text()='true']")
        |> (not << isNull)

    let isPackable (projFilePath: string) : bool =
        projFilePath |> hasProperty "IsPackable"

    let isPublishable (projFilePath: string) : bool =
        projFilePath |> hasProperty "IsPublishable"

    let isTest (projFilePath: string) : bool =
        projFilePath |> hasProperty "IsTestProject"

    let warnIgnored (projFilePath: string) : unit =
        Trace.traceImportantfn
            $"WARNING: {projFilePath} is a leaf project not matching the inclusion criteria. The project will be ignored."
