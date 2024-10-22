#r "paket: nuget Fake.Core.Environment = 6.1.1"

namespace Arquidev.Dbt

open Fake.Core

#load "discover.fsx"
#load "git.fsx"
#load "solution.fsx"

open System.Xml.XPath

type Mode =
    | All
    | Diff

    static member Parse(value: string) =
        match value with
        | "all" -> All
        | "diff" -> Diff
        | m -> failwithf $"Mode not supported: %s{m}"

    static member FromEnv() =
        Environment.environVarOrDefault "DBT_MODE" "diff" |> Mode.Parse

type Project =
    abstract member Path: string

[<AutoOpen>]
module Types =

    type Project with
        static member Is<'a when 'a :> Project>(p: Project) : bool = p :? 'a

        static member WarnIgnored(p: Project) : unit =
            Trace.traceImportantfn
                $"WARNING: {p.Path} is a leaf project not matching the inclusion criteria. The project will be ignored."


type ProjectSelector =
    abstract member Pattern: string
    abstract member GetParentProjectsForPaths: dirs: string seq -> Project seq
    abstract member Filter: projects: Project seq -> Project seq

type NodeProject(path: string) =
    override _.ToString (): string = path

    interface Project with
        member _.Path = path

type DotnetProject(path: string) =


    static member hasProperty (propertyName: string) (projPath: string) : bool =
        let xp = XPathDocument(projPath)
        let n = xp.CreateNavigator()

        n.SelectSingleNode($"/Project/PropertyGroup[*]/{propertyName}[text()='true']")
        |> (not << isNull)

    static member IsPublishable(p: DotnetProject) =
        DotnetProject.hasProperty "IsPublishable" (p :> Project).Path

    static member IsPackable(p: DotnetProject) =
        DotnetProject.hasProperty "IsPackable" (p :> Project).Path

    static member IsTest(p: DotnetProject) =
        DotnetProject.hasProperty "IsTestProject" (p :> Project).Path

    override _.ToString (): string = path
    interface Project with
        member _.Path = path


type NodeSelector() =

    interface ProjectSelector with

        member _.Pattern = "package.json"

        member x.GetParentProjectsForPaths(dirPaths: string seq) : Project seq =
            dirPaths
            |> Discover.uniqueParentProjectPaths (x :> ProjectSelector).Pattern
            |> Seq.map (fun p -> NodeProject(p) :> Project)

        member _.Filter(projects: Project seq) : Project seq = projects

type DotnetSelector(slnPath: string, isLeafProject: DotnetProject -> bool) =
    interface ProjectSelector with

        member _.Pattern = "*.*sproj"

        member x.GetParentProjectsForPaths(dirPaths: string seq) : Project seq =

            let projs = Solution.makeDependencyTree slnPath

            dirPaths
            |> Discover.uniqueParentProjectPaths (x :> ProjectSelector).Pattern
            |> Seq.collect (Solution.findLeafDependants projs (fun path -> isLeafProject (DotnetProject(path))))
            |> Seq.distinct
            |> Seq.toList
            |> Seq.map (fun p -> DotnetProject(p) :> Project)

        member _.Filter(projects: Project seq) : Project seq =
            let projects =
                projects |> Seq.filter Project.Is<DotnetProject> |> Seq.cast<DotnetProject>

            printfn "AAA>>>> %A" projects

            let projectsExcludingTests = projects |> Seq.filter (DotnetProject.IsTest >> not)

            printfn "DD>>>> %A" projectsExcludingTests

            let requiredProjects = projectsExcludingTests |> Seq.filter (isLeafProject)

            for project in projectsExcludingTests |> Seq.except requiredProjects do
                Project.WarnIgnored(project :> Project)

            requiredProjects |> Seq.cast<Project>

[<RequireQualifiedAccess>]
module Pipeline =

    let run (selectors: ProjectSelector list) =

        let dirs =
            match Mode.FromEnv() with
            | Diff -> DiffSpec.FromEnv() |> Git.dirsFromDiff
            | All -> Git.allDirs ()

        selectors
        |> Seq.collect (fun s -> s.GetParentProjectsForPaths dirs |> s.Filter)
        |> Seq.toList
