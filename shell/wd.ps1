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
    if (-not (Get-Command 'wade' -ErrorAction SilentlyContinue)) {
        Write-Error "wade not found."
        return
    }

    $tmp = (New-TemporaryFile).FullName
    wade --cwd-file="$tmp" @args
    $dir = Get-Content -Path $tmp -Encoding UTF8
    Remove-Item -Path $tmp -ErrorAction SilentlyContinue
    if ($dir -ne $PWD.Path -and (Test-Path -LiteralPath $dir -PathType Container)) {
        Set-Location -LiteralPath $dir
    }
}
