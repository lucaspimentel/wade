# Shell wrapper for wade — cd on exit
# Press q to quit and cd to the last directory; press Q to quit without cd.
# Add to ~/.config/fish/functions/wd.fish or source in config.fish

function wd
    set tmp (mktemp)
    wade --cwd-file="$tmp" $argv
    set ret $status
    if test -f "$tmp"
        set dir (cat "$tmp")
        rm -f "$tmp"
        if test -n "$dir" -a -d "$dir"
            cd "$dir"
        end
    end
    return $ret
end
