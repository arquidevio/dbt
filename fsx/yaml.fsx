#r "paket: 
    nuget Queil.FsYaml ~> 5.0"

namespace Arquidev.Dbt

open FsYaml

[<RequireQualifiedAccess>]
module Yaml =

    let write (value: 'a) : string = Yaml.dump value

    let read<'a> (value: string) : 'a = Yaml.load<'a> value
