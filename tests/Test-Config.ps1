#Requires -Version 7
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path $PSScriptRoot -Parent
$project  = Join-Path $repoRoot 'src/Wade'
$tempDir  = $null
$failures = 0

function Assert-Config {
    param(
        [string]   $Name,
        [string[]] $RunArgs,
        [string]   $ConfigFile = $null,
        [hashtable]$Env        = @{},
        [bool]     $ShowIcons        = $true,
        [bool]     $ImagePreviews    = $false,
        [string]   $StartPath        = $null
    )

    $saved = @{}
    foreach ($k in $Env.Keys) {
        $saved[$k] = [System.Environment]::GetEnvironmentVariable($k)
        [System.Environment]::SetEnvironmentVariable($k, $Env[$k])
    }

    try {
        $allArgs = @('run', '--project', $project, '--no-build', '--')
        if ($ConfigFile) { $allArgs += "--config-file=$ConfigFile" }
        $allArgs += '--show-config'
        $allArgs += $RunArgs

        $raw = & dotnet @allArgs 2>&1
        if ($LASTEXITCODE -ne 0) {
            Write-Host "  FAIL [$Name]: dotnet run exited $LASTEXITCODE`n       output: $raw" -ForegroundColor Red
            $script:failures++
            return
        }

        $cfg = $raw | ConvertFrom-Json

        $ok = $true
        if ($cfg.show_icons_enabled    -ne $ShowIcons)     { $ok = $false; Write-Host "  FAIL [$Name]: show_icons_enabled expected $ShowIcons got $($cfg.show_icons_enabled)" -ForegroundColor Red }
        if ($cfg.image_previews_enabled -ne $ImagePreviews) { $ok = $false; Write-Host "  FAIL [$Name]: image_previews_enabled expected $ImagePreviews got $($cfg.image_previews_enabled)" -ForegroundColor Red }
        if ($StartPath -and ($cfg.start_path -ne $StartPath)) { $ok = $false; Write-Host "  FAIL [$Name]: start_path expected '$StartPath' got '$($cfg.start_path)'" -ForegroundColor Red }

        if ($ok) {
            Write-Host "  PASS [$Name]" -ForegroundColor Green
        } else {
            $script:failures++
        }
    } finally {
        foreach ($k in $saved.Keys) {
            [System.Environment]::SetEnvironmentVariable($k, $saved[$k])
        }
    }
}

try {
    $tempDir = Join-Path ([System.IO.Path]::GetTempPath()) "wade-test-$([System.IO.Path]::GetRandomFileName())"
    New-Item -ItemType Directory -Path $tempDir | Out-Null

    Write-Host "Building Wade..." -ForegroundColor Cyan
    & dotnet build $repoRoot/Wade.slnx -c Debug -v quiet
    if ($LASTEXITCODE -ne 0) { throw "Build failed" }

    Write-Host "`nRunning config integration tests..." -ForegroundColor Cyan

    # 1. Defaults
    Assert-Config -Name 'Defaults' `
        -RunArgs @() `
        -ShowIcons $true -ImagePreviews $false

    # 2. Config file sets bools to true
    $cfgBothTrue = Join-Path $tempDir 'both-true.toml'
    Set-Content $cfgBothTrue "show_icons_enabled = true`nimage_previews_enabled = true"
    Assert-Config -Name 'ConfigFile sets both true' `
        -RunArgs @() -ConfigFile $cfgBothTrue `
        -ShowIcons $true -ImagePreviews $true

    # 3. Env var overrides config file
    $cfgIconsTrue = Join-Path $tempDir 'icons-true.toml'
    Set-Content $cfgIconsTrue 'show_icons_enabled = true'
    Assert-Config -Name 'EnvVar overrides config (false wins)' `
        -RunArgs @() -ConfigFile $cfgIconsTrue `
        -Env @{ WADE_SHOW_ICONS_ENABLED = 'false' } `
        -ShowIcons $false -ImagePreviews $false

    # 4. CLI flag overrides env var
    Assert-Config -Name 'CLI flag overrides env var' `
        -RunArgs @('--show-icons-enabled') `
        -Env @{ WADE_SHOW_ICONS_ENABLED = 'false' } `
        -ShowIcons $true -ImagePreviews $false

    # 5. --no-* flags negate
    Assert-Config -Name '--no-show-icons-enabled negates' `
        -RunArgs @('--no-show-icons-enabled') `
        -Env @{ WADE_SHOW_ICONS_ENABLED = 'true' } `
        -ShowIcons $false -ImagePreviews $false

    Assert-Config -Name '--no-image-previews-enabled negates' `
        -RunArgs @('--no-image-previews-enabled') `
        -Env @{ WADE_IMAGE_PREVIEWS_ENABLED = 'true' } `
        -ShowIcons $true -ImagePreviews $false

    # 6. --flag=true / --flag=false syntax
    Assert-Config -Name '--show-icons-enabled=true' `
        -RunArgs @('--show-icons-enabled=true') `
        -ShowIcons $true -ImagePreviews $false

    Assert-Config -Name '--show-icons-enabled=false overrides env' `
        -RunArgs @('--show-icons-enabled=false') `
        -Env @{ WADE_SHOW_ICONS_ENABLED = 'true' } `
        -ShowIcons $false -ImagePreviews $false

    Assert-Config -Name '--image-previews-enabled=true' `
        -RunArgs @('--image-previews-enabled=true') `
        -ShowIcons $true -ImagePreviews $true

    Assert-Config -Name '--image-previews-enabled=false overrides env' `
        -RunArgs @('--image-previews-enabled=false') `
        -Env @{ WADE_IMAGE_PREVIEWS_ENABLED = 'true' } `
        -ShowIcons $true -ImagePreviews $false

    # 7. Positional path argument
    Assert-Config -Name 'Positional path' `
        -RunArgs @($tempDir) `
        -StartPath $tempDir

    Assert-Config -Name 'Positional path with flags' `
        -RunArgs @('--show-icons-enabled', $tempDir) `
        -ShowIcons $true -StartPath $tempDir

    # 8. All three tiers combined (CLI wins)
    $cfgFalse = Join-Path $tempDir 'all-false.toml'
    Set-Content $cfgFalse "show_icons_enabled = false`nimage_previews_enabled = false"
    Assert-Config -Name 'Three-tier: CLI wins over env and config' `
        -RunArgs @('--show-icons-enabled', '--image-previews-enabled') `
        -ConfigFile $cfgFalse `
        -Env @{ WADE_SHOW_ICONS_ENABLED = 'false'; WADE_IMAGE_PREVIEWS_ENABLED = 'false' } `
        -ShowIcons $true -ImagePreviews $true

} finally {
    if ($tempDir -and (Test-Path $tempDir)) {
        Remove-Item $tempDir -Recurse -Force
    }
}

Write-Host ''
if ($failures -eq 0) {
    Write-Host "All tests passed." -ForegroundColor Green
    exit 0
} else {
    Write-Host "$failures test(s) failed." -ForegroundColor Red
    exit 1
}
