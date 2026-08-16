<#
.SYNOPSIS
    Build and launch Aerodrome.

.DESCRIPTION
    Finds the Godot .NET editor, builds the game assembly, and runs it.

    It deliberately resolves the REAL Godot executable instead of the winget shim
    in WinGet\Links. Godot works out where its GodotSharp folder lives from its own
    executable path, and the shim sits somewhere else, so launching through the
    shim fails with "Assemblies not found" and a signal 11 crash.

.EXAMPLE
    .\run.ps1
    .\run.ps1 -Editor
    .\run.ps1 -Capture .\shots
#>
[CmdletBinding()]
param(
    # Open the Godot editor instead of playing.
    [switch]$Editor,
    # Run the scripted demo and write screenshots here, then quit.
    [string]$Capture,
    [string]$Resolution = '1600x900',
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

function Resolve-Godot {
    $candidates = @()

    $packages = Join-Path $env:LOCALAPPDATA 'Microsoft\WinGet\Packages'
    if (Test-Path $packages) {
        $candidates += Get-ChildItem $packages -Recurse -Filter 'Godot_v*_mono_win64.exe' -ErrorAction SilentlyContinue
    }
    foreach ($dir in @('C:\Godot', 'D:\Godot', (Join-Path $env:LOCALAPPDATA 'Programs\Godot'))) {
        if (Test-Path $dir) {
            $candidates += Get-ChildItem $dir -Recurse -Filter 'Godot*mono*.exe' -ErrorAction SilentlyContinue
        }
    }

    # Skip the _console variants for normal play; they spawn a second window.
    $exe = $candidates | Where-Object { $_.Name -notmatch '_console' } |
           Sort-Object Name -Descending | Select-Object -First 1

    if (-not $exe) {
        throw "Could not find a Godot .NET build. Install it with: winget install GodotEngine.GodotEngine.Mono"
    }
    return $exe.FullName
}

$godot = Resolve-Godot
Write-Host "godot   $godot" -ForegroundColor DarkGray

if (-not $SkipBuild) {
    Write-Host "build   game/Aerodrome.Game.csproj" -ForegroundColor DarkGray
    dotnet build (Join-Path $root 'game\Aerodrome.Game.csproj') --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw "Build failed." }
}

$gameDir = Join-Path $root 'game'

# Not $args: that is a PowerShell automatic variable.
$godotArgs = @('--path', $gameDir)

if ($Editor) {
    $godotArgs += '--editor'
}
else {
    $godotArgs += @('--resolution', $Resolution)
    if ($Capture) {
        $full = (New-Item -ItemType Directory -Force -Path $Capture).FullName
        $env:AERODROME_SHOT_DIR = $full
        Write-Host "capture $full" -ForegroundColor DarkGray
    }
}

& $godot @godotArgs
