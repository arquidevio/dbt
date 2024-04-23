namespace Arquidev.Dbt

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

    let requireIf
        (isRequired: string -> bool)
        (isExcluded: string -> bool)
        (projectPath: string)
        : bool * string list =

        if projectPath |> isExcluded then
            false, []
        else
            match projectPath |> isRequired with
            | true -> true, []
            | false ->
                false,
                [ $"WARNING: {projectPath} is a leaf project neither required nor included - it will be ignored."
                  "To get rid of this warning either mark the project as required or excluded or remove it if it's dead code. You can also ignore this warning." ]
