#!/usr/bin/env pwsh
#Requires -Version 7.0

<#
.SYNOPSIS
    Builds and installs wade from source.

.DESCRIPTION
    This script builds the wade TUI file browser from source and installs it
    to ~/.local/bin/wade (or ~/.local/bin/wade.exe on Windows).

    Requirements:
    - PowerShell 7.0+
    - .NET 10 SDK

.PARAMETER Force
    Skip confirmation prompts and overwrite existing installation.

.EXAMPLE
    ./install-local.ps1

    Builds and installs wade to ~/.local/bin

.EXAMPLE
    ./install-local.ps1 -Force

    Builds and installs, overwriting any existing installation.
#>

[CmdletBinding()]
param(
    [switch]$Force
)

$ErrorActionPreference = 'Stop'

# Determine runtime identifier based on platform
$rid = if ($IsWindows) {
    'win-x64'
} elseif ($IsMacOS) {
    if ([System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture -eq 'Arm64') {
        'osx-arm64'
    } else {
        'osx-x64'
    }
} elseif ($IsLinux) {
    'linux-x64'
} else {
    throw "Unsupported platform"
}

$exeName = if ($IsWindows) { 'wade.exe' } else { 'wade' }

Write-Host "Building wade for $rid..." -ForegroundColor Cyan

# Check for .NET SDK
try {
    $dotnetVersion = & dotnet --version 2>&1
    Write-Host ".NET SDK version: $dotnetVersion" -ForegroundColor Cyan
} catch {
    Write-Host "Error: .NET SDK not found. Please install .NET 10 SDK or later." -ForegroundColor Red
    Write-Host "Download from: https://dotnet.microsoft.com/download/dotnet/10.0" -ForegroundColor Cyan
    exit 1
}

# Build and publish the project
$projectPath = Join-Path $PSScriptRoot 'src/Wade/Wade.csproj'
$publishPath = Join-Path $PSScriptRoot 'artifacts/publish'

try {
    dotnet publish $projectPath `
        -c Release `
        -r $rid `
        --output $publishPath

    if ($LASTEXITCODE -ne 0) {
        throw "Build failed with exit code $LASTEXITCODE"
    }
} catch {
    Write-Host "Error: Failed to build project: $_" -ForegroundColor Red
    exit 1
}

# Verify executable exists
$exePath = Join-Path $publishPath $exeName

if (-not (Test-Path $exePath)) {
    Write-Host "Error: Build succeeded but executable not found at: $exePath" -ForegroundColor Red
    exit 1
}

Write-Host "Build successful!" -ForegroundColor Green

# Install to ~/.local/bin
$installDir = Join-Path $HOME '.local/bin'
$installPath = Join-Path $installDir $exeName

# Create installation directory if it doesn't exist
if (-not (Test-Path $installDir)) {
    Write-Host "Creating installation directory: $installDir" -ForegroundColor Cyan
    New-Item -ItemType Directory -Path $installDir -Force | Out-Null
}

# Check for existing installation
if (Test-Path $installPath) {
    if (-not $Force) {
        Write-Host "wade is already installed at: $installPath" -ForegroundColor Yellow
        $response = Read-Host "Overwrite existing installation? (y/N)"
        if ($response -notmatch '^[Yy]') {
            Write-Host "Installation cancelled." -ForegroundColor Cyan
            exit 0
        }
    }
    Write-Host "Removing existing installation..." -ForegroundColor Yellow
    Remove-Item $installPath -Force
}

# Copy executable to installation directory
Write-Host "Installing to: $installPath" -ForegroundColor Cyan
Copy-Item $exePath $installPath -Force

# Set executable permissions on Unix systems
if (-not $IsWindows) {
    chmod +x $installPath
}

Write-Host "`nInstallation complete!" -ForegroundColor Green
Write-Host "`nTo use wade, ensure ~/.local/bin is in your PATH:" -ForegroundColor Cyan

if ($IsWindows) {
    Write-Host @"

Add to your PowerShell profile ($PROFILE):
    `$env:PATH = "`$HOME\.local\bin;`$env:PATH"

Then restart your shell or run:
    . `$PROFILE

"@ -ForegroundColor White
} else {
    Write-Host @"

Add to your shell's rc file (~/.bashrc, ~/.zshrc, etc.):
    export PATH="`$HOME/.local/bin:`$PATH"

Then restart your shell or source the file.

"@ -ForegroundColor White
}

Write-Host "Usage:" -ForegroundColor Cyan
Write-Host "    wade              # open in current directory" -ForegroundColor White
Write-Host "    wade C:\Users     # open in a specific directory" -ForegroundColor White
