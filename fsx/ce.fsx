namespace Arquidev.Dbt

module Ce =

    type CoreBuilder() =
        member inline _.Zero() : 'a list = []
        member inline _.Delay(f: unit -> 'a list) = f ()
        member inline _.Yield(x: 'a list) = x
        member inline _.Yield(x: 'a) = [ x ]
        member inline _.Yield(x: unit) = []
        member inline _.Combine(a, b) = a @ b
        member inline _.For(xs, f: unit -> 'a list) = (xs |> Seq.toList) @ f ()
