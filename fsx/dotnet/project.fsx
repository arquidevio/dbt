#load "solution.fsx"
#load "../types.fsx"
#load "../plan.fsx"

namespace Arquidev.Dbt

open System.Xml.XPath

[<RequireQualifiedAccess>]
module DotnetProject =

    let hasProperty (propertyName: string) (projPath: string) : bool =
        let xp = XPathDocument projPath
        let n = xp.CreateNavigator()

        n.SelectSingleNode $"/Project/PropertyGroup[*]/{propertyName}[text()='true']"
        |> (not << isNull)

    let isPublishable (p: string) = hasProperty "IsPublishable" p

    let isPackable (p: string) = hasProperty "IsPackable" p

    let isTest (p: string) = hasProperty "IsTestProject" p


[<AutoOpen>]
module DotnetSelectors =

    type selector with
        static member dotnet: Selectors = Selectors()

    and Selectors() =
        member _.generic =
            selector.define "dotnet" {
                pattern "*.*sproj"
                required_when (fun _ -> true)
                ignored_when DotnetProject.isTest
                expand_leafs (fun selector path ->
                    let projs = Solution.makeDependencyTree (Solution.findInCwd ())
                    path |> Solution.findLeafDependants projs selector.isRequired)
            }

        /// All C#/F# projects with IsPublishable=true
        member x.image =
            selector.extend x.generic { required_when DotnetProject.isPublishable }

        /// All C#/F# projects with IsPackable=true
        member x.nuget = selector.extend x.generic { required_when DotnetProject.isPackable }
