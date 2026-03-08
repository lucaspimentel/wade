#!/usr/bin/env bash
# Shell wrapper for wade — cd on exit
# Press q to quit and cd to the last directory; press Q to quit without cd.
# Source this file in your .bashrc or .zshrc:
#   source /path/to/wd.sh

wd() {
    if ! command -v wade &> /dev/null; then
        echo "wade not found." >&2
        return 1
    fi

    local tmp
    tmp="$(mktemp)"
    wade --cwd-file="$tmp" "$@"
    local ret=$?
    if [ -f "$tmp" ]; then
        local dir
        dir="$(cat "$tmp")"
        rm -f "$tmp"
        if [ -n "$dir" ] && [ -d "$dir" ]; then
            cd "$dir" || return
        fi
    fi
    return $ret
}
