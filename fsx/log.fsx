namespace Arquidev.Dbt

#load "env.fsx"

[<RequireQualifiedAccess>]
module Log =
    [<RequireQualifiedAccess>]
    type LogLevel =
        | debug
        | info
        | warn
        | error

    type LogEnv =
        { [<Default("info")>]
          DBT_LOG_LEVEL: LogLevel }

    let private env = Lazy<LogEnv>(fun () -> Env.get<LogEnv> ())

    let level () = env.Value.DBT_LOG_LEVEL

    let output<'a> level fmt =
        Printf.kprintf<unit, 'a>
            (fun str ->
                if level >= env.Value.DBT_LOG_LEVEL then
                    printfn "%s" str)
            fmt

    let debug<'a> = output<'a> LogLevel.debug
    let info<'a> = output<'a> LogLevel.info
    let warn<'a> = output<'a> LogLevel.warn
    let error<'a> = output<'a> LogLevel.error

    let header message =
        let width = 80
        let line = String.replicate width "-"
        let msgLength = String.length message
        let padSize = max 0 ((width - msgLength) / 2)
        let padding = String.replicate padSize " "

        printfn "%s" line
        printfn "%s%s" padding message
        printfn "%s" line
