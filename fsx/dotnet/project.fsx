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

    type SelectorBuilderDefaults with 
         member _.dotnet: Selectors = Selectors()
    and Selectors() =
        member _.generic =
            selector {
                id "dotnet"
                pattern "*.*sproj"
                required_when (fun _ -> true)
                ignored_when DotnetProject.isTest

                expand_leafs (fun selector path ->
                    let projs = Solution.makeDependencyTree (Solution.findInCwd ())
                    path |> Solution.findLeafDependants projs selector.isRequired)
            }

        /// All C#/F# projects with IsPublishable=true
        member x.image =
            selector {
                required_when DotnetProject.isPublishable
                extend x.generic
            }

        /// All C#/F# projects with IsPackable=true
        member x.nuget =
            selector {
                required_when DotnetProject.isPackable
                extend x.generic
            }
