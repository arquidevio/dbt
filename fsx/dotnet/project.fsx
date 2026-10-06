#load "solution.fsx"
#load "../plan.builder.fsx"

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
  open System.Collections.Concurrent
  open System.Collections.Generic

  type SelectorBuilderDefaults with
    member _.dotnet: Selectors = Selectors()

  and Selectors() =
    let trees = ConcurrentDictionary<string, IDictionary<string, string list>>()

    let expandWith findDependants (ctx: LeafExpansionContext) =
      let projs = trees.GetOrAdd(Solution.findInCwd (), Solution.makeDependencyTree)
      ctx.projectPath |> findDependants projs ctx.selector.isRequired

    member _.generic =
      selector {
        id "dotnet"
        pattern "*.*sproj"
        required_when (fun _ -> true)
        ignored_when DotnetProject.isTest
        expand_leafs (expandWith Solution.findLeafDependants)
      }

    /// All C#/F# projects with IsPublishable=true
    member x.image =
      selector {
        required_when DotnetProject.isPublishable
        extend x.generic
      }

    /// All C#/F# projects with IsPackable=true — traverses the full dependency
    /// graph collecting all packable projects rather than stopping at the first match
    member x.nuget =
      selector {
        required_when DotnetProject.isPackable
        extend x.generic
        expand_leafs (expandWith Solution.findAllLeafDependants)
      }
