<#
.SYNOPSIS
    Cuts a new release of DCMultiplayer.

.DESCRIPTION
    1. Verifies the working tree is clean and we're on `main`.
    2. Verifies ModInfo.Version matches the requested version.
    3. Rebuilds the mod (clean), publishes the installer (single-file
       self-contained), and creates the distribution zip in Tools/dist/.
    4. Creates a git tag and pushes it. The remote workflow then opens a
       GitHub release shell; you upload the zip via the UI (or `gh`).

.PARAMETER Version
    Semver string without the `v` prefix, e.g. "0.0.9".

.EXAMPLE
    pwsh ./scripts/release.ps1 0.0.9
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $Version
)

$ErrorActionPreference = 'Stop'

$repo = Resolve-Path (Join-Path $PSScriptRoot "..")
$modProj = Join-Path $repo "DCMultiplayer.Mod"
$installerProj = Join-Path $repo "DCInstaller"
$dist = Join-Path $repo "dist"

Write-Host "== Releasing v$Version =="

# ── pre-flight ─────────────────────────────────────────────────────────
Push-Location $repo
try
{
    $branch = git rev-parse --abbrev-ref HEAD
    if ($branch -ne 'main')
    {
        throw "Refusing to release from branch '$branch'. Switch to main first."
    }

    $dirty = git status --porcelain
    if ($dirty)
    {
        throw "Working tree is dirty. Commit or stash before releasing.`n$dirty"
    }

    if (git tag --list "v$Version")
    {
        throw "Tag v$Version already exists. Bump the version or delete the tag."
    }

    # ModInfo.Version must already be bumped in source
    $modInfo = Get-Content (Join-Path $modProj "ModInfo.cs") -Raw
    if ($modInfo -notmatch [regex]::Escape("Version = `"$Version`""))
    {
        throw "ModInfo.cs Version != $Version. Bump it and commit before releasing."
    }

    # Make sure the game isn't running and won't lock the deploy step
    Get-Process -Name "Data Center","DCMultiplayer-Installer" -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Seconds 1

    # ── build ─────────────────────────────────────────────────────────
    $env:DOTNET_NOLOGO = "1"
    if (Get-Command dotnet -ErrorAction SilentlyContinue) { } else {
        $env:Path = "$env:LOCALAPPDATA\Microsoft\dotnet;" + $env:Path
    }

    Write-Host "[1/4] Clean rebuild of DCMultiplayer.Mod ..."
    Push-Location $modProj
    try
    {
        Remove-Item -Recurse -Force .\bin, .\obj -ErrorAction SilentlyContinue
        & dotnet build -c Release | Out-Host
        if ($LASTEXITCODE -ne 0) { throw "mod build failed" }
    }
    finally { Pop-Location }

    Write-Host "[2/4] Publishing single-file installer ..."
    Push-Location $installerProj
    try
    {
        Remove-Item -Recurse -Force .\bin, .\obj -ErrorAction SilentlyContinue
        & dotnet publish -c Release | Out-Host
        if ($LASTEXITCODE -ne 0) { throw "installer publish failed" }
    }
    finally { Pop-Location }

    Write-Host "[3/4] Packaging zip ..."
    if (-not (Test-Path $dist)) { New-Item -ItemType Directory -Path $dist | Out-Null }
    Get-ChildItem $dist -Filter "DCMultiplayer-*.zip" | Remove-Item -Force
    $zip = Join-Path $dist "DCMultiplayer-v$Version-win-x64.zip"
    $exe = Join-Path $installerProj "bin\Release\net8.0\win-x64\publish\DCMultiplayer-Installer.exe"
    $readme = Join-Path $installerProj "README.txt"
    Compress-Archive -Path $exe, $readme -DestinationPath $zip -CompressionLevel Optimal
    $zipSize = (Get-Item $zip).Length
    Write-Host "      $zip ($zipSize bytes)"

    # ── tag & push ────────────────────────────────────────────────────
    Write-Host "[4/4] Tagging v$Version and pushing ..."
    git tag -a "v$Version" -m "v$Version"
    git push origin "v$Version"

    Write-Host ""
    Write-Host "== Done =="
    Write-Host ""
    Write-Host "Now open the release page and drop the zip:"
    Write-Host "  https://github.com/andrediashexa/datacenter-multiplayer/releases/edit/v$Version"
    Write-Host ""
    Write-Host "Or with gh CLI:"
    Write-Host "  gh release upload v$Version `"$zip`" --clobber"
}
finally { Pop-Location }
