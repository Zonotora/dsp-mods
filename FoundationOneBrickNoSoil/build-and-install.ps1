param(
    [Parameter(Mandatory = $true)]
    [string]$GameDir
)

$ErrorActionPreference = 'Stop'

$required = @(
    'DSPGAME_Data\Managed\Assembly-CSharp.dll',
    'DSPGAME_Data\Managed\UnityEngine.CoreModule.dll',
    'DSPGAME_Data\Managed\UnityEngine.dll',
    'BepInEx\core\BepInEx.dll',
    'BepInEx\core\0Harmony.dll'
)
foreach ($relative in $required) {
    $fullPath = Join-Path $GameDir $relative
    if (-not (Test-Path $fullPath)) {
        throw "Missing required DLL: $fullPath. Check GameDir and install BepInEx 5."
    }
}

$project = Join-Path $PSScriptRoot 'FoundationOneBrickNoSoil.csproj'
& dotnet build $project -c Release "-p:DSPGameDir=$GameDir" "-p:BepInExCoreDir=$(Join-Path $GameDir 'BepInEx\core')"
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }

$builtDll = Join-Path $PSScriptRoot 'bin\Release\net472\FoundationOneBrickNoSoil.dll'
if (-not (Test-Path $builtDll)) { throw 'Build succeeded but output DLL not found.' }

$pluginDir = Join-Path $GameDir 'BepInEx\plugins\FoundationOneBrickNoSoil'
New-Item -ItemType Directory -Force -Path $pluginDir | Out-Null
Copy-Item $builtDll (Join-Path $pluginDir 'FoundationOneBrickNoSoil.dll') -Force
Write-Host "Installed: $pluginDir\FoundationOneBrickNoSoil.dll"
Write-Warning 'Disable/remove the original UnlimitedFoundations mod: its unlimited-foundation patch conflicts with this mod.'
