# Sube un artefacto (APK, pack, captura) a TCloud/Telegram en streaming.
#
#   .\tools\upload-file.ps1 -File app\build\outputs\apk\debug\app-debug.apk -Notes "resumen"
#   .\tools\upload-file.ps1 -File ruta\pack.sensepack -Front
#
# Carga .tcloud.env solo en el proceso hijo: el token no se imprime ni se persiste.

param(
    [Parameter(Mandatory = $true)][string]$File,
    [string]$Notes = "",
    [switch]$Front,
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$envFile = Join-Path $root ".tcloud.env"
if (-not (Test-Path $envFile)) { throw "Falta .tcloud.env en $root" }

foreach ($line in [IO.File]::ReadAllLines($envFile)) {
    $t = $line.Trim()
    if (-not $t -or $t.StartsWith("#")) { continue }
    $i = $t.IndexOf("=")
    if ($i -lt 1) { continue }
    $k = $t.Substring(0, $i).Trim()
    if ($k -notlike "TCLOUD_*") { continue }
    $v = $t.Substring($i + 1).Trim().Trim('"').Trim("'")
    [Environment]::SetEnvironmentVariable($k, $v, "Process")
}

$path = if ([IO.Path]::IsPathRooted($File)) { $File } else { Join-Path $root $File }
if (-not (Test-Path $path)) { throw "No existe el fichero: $path" }

$project = $env:TCLOUD_PROJECT
if (-not $project) { $project = Split-Path -Leaf $root }

# Versión solo si es un APK con Gradle al lado.
$label = Split-Path -Leaf $path
$gradle = Join-Path $root "app\build.gradle.kts"
if (-not (Test-Path $gradle)) { $gradle = Join-Path $root "app\build.gradle" }
if ($path -like "*.apk" -and (Test-Path $gradle)) {
    $m = Select-String -Path $gradle -Pattern 'versionName\s*=?\s*["'']([^"'']+)["'']' | Select-Object -First 1
    if ($m) { $label = "$($m.Matches[0].Groups[1].Value) (debug)" }
}

$stamp = Get-Date -Format "yyyy-MM-dd HH:mm"
$caption = "$project | $label | $stamp"
if ($Notes) { $caption = "$caption | $Notes" }

$pyArgs = @((Join-Path $root "tools\tcloud_upload.py"), $path, "--caption", $caption)
if ($Front) { $pyArgs += "--front" }

$size = [math]::Round((Get-Item $path).Length / 1MB, 1)
Write-Host "Subiendo $path ($size MB)"
Write-Host "Caption: $caption"
if ($DryRun) { exit 0 }
& python @pyArgs
exit $LASTEXITCODE
