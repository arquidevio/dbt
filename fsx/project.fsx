#r "paket: nuget Fake.Core.Trace >= 6.0.0"

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

    let isRequired
        (isRequiredPorperty: string)
        (isRequired: string -> bool)
        (isTest: string -> bool)
        (projectPath: string)
        : bool =

        if projectPath |> isTest then
            false
        else
            match projectPath |> isRequired with
            | true -> true
            | false ->
                Trace.traceImportantfn
                    $"\nWARNING: {projectPath} is a leaf project without {isRequiredPorperty}=true property and will be ignored."

                Trace.traceImportantfn
                    "Either mark the project as publishable or remove it if it's dead code. You can also ignore this warning."

                false
