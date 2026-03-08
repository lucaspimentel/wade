<#
.SYNOPSIS
    Shell wrapper for wade that changes directory on exit.

.DESCRIPTION
    Launches the wade TUI file browser and changes the current directory to the
    last browsed directory when wade exits. Press q to quit and cd; press Q to
    quit without cd.

    Dot-source this file in your $PROFILE:
        . /path/to/wd.ps1

.PARAMETER Path
    Optional starting directory to browse. Defaults to the current directory.

.EXAMPLE
    wd

    Opens wade in the current directory.

.EXAMPLE
    wd C:\Users

    Opens wade in C:\Users.
#>

function wd {
    $tmp = [System.IO.Path]::GetTempFileName()
    wade --cwd-file="$tmp" @args
    if (Test-Path $tmp) {
        $dir = Get-Content $tmp -Raw
        Remove-Item $tmp -ErrorAction SilentlyContinue
        if ($dir -and (Test-Path $dir -PathType Container)) {
            Set-Location $dir
        }
    }
}
