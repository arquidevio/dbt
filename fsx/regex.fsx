namespace Arquidev.Dbt

open System.Text.RegularExpressions

[<AutoOpen>]
module Regex =

    let (|ParseRegex|_|) regex str =
        let m = Regex(regex).Match str

        if m.Success then
            Some(List.tail [ for x in m.Groups -> x.Value ])
        else
            None

    [<RequireQualifiedAccess>]
    module Regex =
        /// Checks if the input string matches the pattern. Pattern gets wrapped in ^$
        let isMatchSafe (input: string) (pattern: string) =
            Regex.IsMatch(input, $"^{pattern.Trim('^').TrimEnd '$'}$")
