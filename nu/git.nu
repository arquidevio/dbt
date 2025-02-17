use std log

const empty_commit = "0000000000000000000000000000000000000000"

let diff_spec = {
    current_commit: ($env.DBT_CURRENT_COMMIT? | default "HEAD"),
    base_commit: ( match $env.DBT_BASE_COMMIT? { $empty_commit => null, $v => $v} ),
    maybe_tag: $env.DBT_MAYBE_TAG?
}

def all_dirs [] {
    git ls-files | lines | each {|f| $f | path dirname } | where (is-not-empty) | uniq
}

def dirs_from_dir [spec: record<current_commit, base_commit, maybe_tag>] {

    print $"Current revision: ($spec.current_commit)"
    let base_refs: list<string> = match $spec {
        {maybe_tag: null, base_commit: $bc } => {
            match $bc {
                null => {
                    print -n "Base revisions(s): "
                    let refs = git show --no-patch --format="%P" $spec.current_commit | split words
                    $refs | print -r
                    $refs
                }
                $ref => {
                  print $"Base revision override: ($ref)"
                  [ $ref ]
                }
            }
        }
        {maybe_tag: $tag } => {
            print $"Tag: ($tag)"
            [ git describe --abbrev=0 --tags "$tag^" ]
        }

    }
    let dirs = (
        $base_refs | each { |b| git diff $spec.current_commit $b --name-status 
                                | lines 
                                | each { path dirname } 
                                | where (is-not-empty) })
                   | flatten
                   | uniq
    let info = match $dirs { 
        [] => "No meaningful changes detected"
        _ => "Detected git changes in:"
    }
    
    print $info
    $dirs | each { print -r }
    $dirs | where { |p| if ($p | path exists) { true } else { print $"WARNING: path '($p)' no longer exists in the repository. Ignoring."; false} }
}

dirs_from_dir $diff_spec
