#r "paket: nuget TypeShape ~> 10"

namespace Arquidev.Dbt

open TypeShape.Core
open System

[<AttributeUsageAttribute(AttributeTargets.Property)>]
type DefaultAttribute(value: string) =
    inherit Attribute()

    member _.Value = value


[<RequireQualifiedAccess>]
module Env =


    let private parser<'V> : string -> 'V =
        fun s ->
            let wrap parse = unbox<'V> (parse s)

            match shapeof<'V> with
            | Shape.String -> wrap id
            | Shape.Int32 -> wrap Int32.Parse
            | Shape.Int64 -> wrap Int64.Parse
            | Shape.Bool ->
                wrap (function
                    | "true"
                    | "TRUE"
                    | "1"
                    | "YES"
                    | "Y" -> true
                    | "false"
                    | "FALSE"
                    | "0"
                    | "NO"
                    | "N" -> false
                    | b -> failwithf $"Value {b} is not a valid boolean")
            | shape -> failwithf $"Not supported: {shape}"

    let private getValue<'R> label (defaultValue: string option) =
        let value = Environment.GetEnvironmentVariable label

        match shapeof<'R> with
        | Shape.FSharpOption shape ->
            let optionElementParser =
                shape.Element.Accept
                    { new ITypeVisitor<string -> 'R> with
                        member _.Visit<'a>() =
                            function
                            | s when String.IsNullOrEmpty s -> unbox<'R> None
                            | s -> unbox<'R> (Some(parser<'a> s)) }

            optionElementParser value
        | Shape.FSharpUnion(:? ShapeFSharpUnion<'R> as shape) ->
            if shape.UnionCases |> Seq.exists (fun c -> c.Arity > 0) then
                failwithf "Only enum unions are supported"

            let unionParser =
                fun s ->
                    let maybeValue (caseName: string) =
                        shape.UnionCases
                        |> Seq.tryFind (fun c ->
                            String.Equals(c.CaseInfo.Name, caseName, StringComparison.OrdinalIgnoreCase))
                        |> Option.map (fun c -> c.CreateUninitialized())

                    let possibleValues =
                        shape.UnionCases |> Seq.map _.CaseInfo.Name |> Seq.toList |> String.concat ", "

                    match maybeValue s with
                    | Some v -> v
                    | None ->
                        match defaultValue with
                        | Some d ->
                            match maybeValue d with
                            | Some v -> v
                            | None ->
                                failwithf
                                    $"Unexpected default value for env variable: {label}={d}. Possible values: {possibleValues} (case-insensitive)"
                        | None ->
                            failwithf
                                $"Unexpected value for env variable: {label}={s}. Possible values: {possibleValues} (case-insensitive)"

            unionParser value
        | _ ->
            let value =
                match value, defaultValue with
                | s, Some d when String.IsNullOrEmpty s -> d |> string
                | s, None when String.IsNullOrEmpty s -> failwithf $"Environment variable is not set: {label}"
                | s, _ -> s

            parser<'R> value

    let get<'T> () =
        match shapeof<'T> with
        | Shape.FSharpRecord(:? ShapeFSharpRecord<'T> as shape) ->
            let record = shape.CreateUninitialized()

            let fieldSetters =
                shape.Fields
                |> Seq.fold
                    (fun setters field ->
                        field.Accept
                            { new IMemberVisitor<'T, ('T -> 'T) list> with
                                member _.Visit(shape: ShapeMember<'T, 'a>) =
                                    (fun r ->
                                        let defaultValue =
                                            let attr =
                                                field.MemberInfo.GetCustomAttributes(typeof<DefaultAttribute>, false)

                                            (match attr with
                                             | [| :? DefaultAttribute as found |] -> Some found
                                             | _ -> None)
                                            |> Option.map _.Value

                                        let fieldValue = getValue<'a> field.Label defaultValue
                                        shape.Set r fieldValue)
                                    :: setters })
                    []

            fieldSetters |> Seq.fold (fun r setter -> setter r) record
        | _ -> failwithf "Not supported"
