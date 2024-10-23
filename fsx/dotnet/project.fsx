namespace Arquidev.Dbt

#load "solution.fsx"
#load "../project.fsx"

open System.IO
open System.Xml.XPath

[<RequireQualifiedAccess>]
module DotnetProject =

    module SafeName =

        let toLowerKebab (projFullPath: string) : string =
            projFullPath
            |> Path.GetDirectoryName
            |> Path.GetFileName
            |> fun p -> p.ToLowerInvariant()
            |> fun p -> p.Split('.')
            |> fun p -> System.String.Join('-', p)

        let toLowerKebabNoRoot (projFullPath: string) : string =
            projFullPath
            |> Path.GetDirectoryName
            |> Path.GetFileName
            |> fun p -> p.ToLowerInvariant()
            |> fun p -> p.Split('.')
            |> fun p -> p[1..]
            |> fun p -> System.String.Join('-', p)

    let hasProperty (propertyName: string) (projPath: string) : bool =
        let xp = XPathDocument(projPath)
        let n = xp.CreateNavigator()

        n.SelectSingleNode($"/Project/PropertyGroup[*]/{propertyName}[text()='true']")
        |> (not << isNull)

    let isPublishable (p: string) = hasProperty "IsPublishable" p

    let isPackable (p: string) = hasProperty "IsPackable" p

    let isTest (p: string) = hasProperty "IsTestProject" p

    let private config =
        { Selector.Default with
            kind = "dotnet"
            pattern = "*.*sproj"
            safeName = SafeName.toLowerKebabNoRoot
            isRequired = isPublishable
            isIgnored = isTest }

    let isDotnet (p: ProjectPath) = p.kind = config.kind

    let Selector slnPath =
        { config with
            expandLeafs =
                fun path ->
                    let projs = Solution.makeDependencyTree slnPath
                    path |> Solution.findLeafDependants projs config.isRequired }
