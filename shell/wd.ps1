# Shell wrapper for wade — cd on exit
# Dot-source this file in your $PROFILE:
#   . /path/to/wd.ps1

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
