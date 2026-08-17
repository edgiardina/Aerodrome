<#
.SYNOPSIS
    Turn a downloaded aircraft model into a game-ready .glb.

.DESCRIPTION
    Wraps tools/prepare_model.py in headless Blender. Finds Blender itself.

    Always inspect first. Downloaded models arrive in arbitrary orientation and
    scale, and the inspection tells you which flags you need.

.EXAMPLE
    # 1. See what you have got.
    .\tools\prepare-model.ps1 -Inspect .\assets\source\camel\scene.gltf

    # 2. Convert, using whatever the inspection told you.
    .\tools\prepare-model.ps1 .\assets\source\camel\scene.gltf -Name camel -RotateX -90

.NOTES
    Output lands in assets/export, which is where Godot looks.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory, Position = 0)][string]$Source,
    [switch]$Inspect,
    [string]$Name = "camel",
    [double]$Length = 5.71,
    [int]$Budget = 8000,
    [double]$RotateX = 0,
    [double]$RotateY = 0,
    [double]$RotateZ = 0,
    [ValidateSet("+X", "-X", "+Y", "-Y", "+Z", "-Z")][string]$NoseAxis = "+X",
    [ValidateSet("+X", "-X", "+Y", "-Y", "+Z", "-Z")][string]$UpAxis = "+Z",
    # Degrees nose up, applied after the nose is turned to +X. Models are built
    # parked on their undercarriage, and a taildragger sits nose-high, so a
    # negative value here levels it onto its flight attitude.
    [double]$Pitch = 0,
    [double]$PropCut = 0.93,
    # Largest texture edge to keep. Sketchfab ships 2K and 4K PBR sets, which is
    # 30 MB of detail for an aircraft that is 150 pixels tall on screen.
    [int]$TextureSize = 1024,
    # Objects to delete before converting, by index or name fragment. Scanned
    # scenes often ship a ground plane or a display stand, and either one ruins
    # the bounding box the scaling depends on.
    [string]$Drop = ""
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent

function Resolve-Blender {
    $candidates = @()
    foreach ($base in @("${env:ProgramFiles}\Blender Foundation",
                        "${env:ProgramFiles(x86)}\Blender Foundation",
                        (Join-Path $env:LOCALAPPDATA 'Microsoft\WinGet\Packages'))) {
        if (Test-Path $base) {
            $candidates += Get-ChildItem $base -Recurse -Filter 'blender.exe' -ErrorAction SilentlyContinue
        }
    }
    $exe = $candidates | Sort-Object FullName -Descending | Select-Object -First 1
    if (-not $exe) {
        throw "Blender not found. Install it with: winget install BlenderFoundation.Blender.LTS.4.5"
    }
    return $exe.FullName
}

$blender = Resolve-Blender
$script = Join-Path $PSScriptRoot 'prepare_model.py'
$sourceFull = (Resolve-Path $Source).Path

Write-Host "blender  $blender" -ForegroundColor DarkGray
Write-Host "source   $sourceFull" -ForegroundColor DarkGray

if ($Inspect) {
    & $blender --background --python $script -- $sourceFull --inspect
    return
}

# Inside the Godot project on purpose. Godot only imports what lives under
# res://, so a .glb sitting in assets/export next door is invisible to it.
$exportDir = Join-Path $root 'game\models'
New-Item -ItemType Directory -Force -Path $exportDir | Out-Null
$out = Join-Path $exportDir "$Name.glb"

# The axis flags use --flag=value, not --flag value. A value like "-X" looks like
# an option to argparse, and it rejects it as a missing argument.
& $blender --background --python $script -- $sourceFull `
    --out $out --name $Name --length $Length --budget $Budget `
    --rotate-x $RotateX --rotate-y $RotateY --rotate-z $RotateZ `
    "--nose-axis=$NoseAxis" "--up-axis=$UpAxis" --prop-cut $PropCut --pitch $Pitch `
    --texture-size $TextureSize "--drop=$Drop"

if ($LASTEXITCODE -ne 0) { throw "Blender failed with exit code $LASTEXITCODE" }

if (Test-Path $out) {
    $kb = [math]::Round((Get-Item $out).Length / 1KB)
    Write-Host "`nwrote $out  ($kb KB)" -ForegroundColor Green
    Write-Host "Godot picks it up automatically on the next run." -ForegroundColor DarkGray
}
