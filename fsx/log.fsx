namespace Arquidev.Dbt

[<AutoOpen>]
module Log =

    module Log =
        type LogLevel = private | Error | Warn | Info | Debug | Trace
        let debug = printfn
        let trace = printfn
        let error = printfn
        let info = printfn
        let warn  = printfn

        let header message =
            let width = 80
            let line = String.replicate width "-"
            let msgLength = String.length message
            let padSize = max 0 ((width - msgLength) / 2)
            let padding = String.replicate padSize " "
            
            printfn "%s" line
            printfn "%s%s" padding message  
            printfn "%s" line