param(
 [Parameter(Mandatory=$true)][string]$Zig,
 [string]$Output = (Join-Path $PSScriptRoot 'silk-to-mp3.exe')
)
$ErrorActionPreference = 'Stop'
$Zig = (Resolve-Path -LiteralPath $Zig).Path
$Output = [IO.Path]::GetFullPath($Output)
$stage = Join-Path ([IO.Path]::GetTempPath()) ('wcc-voice-build-' + [Guid]::NewGuid().ToString('N'))
$version = & $Zig version
if ($LASTEXITCODE -ne 0 -or $version -ne '0.14.1') { throw 'Use Zig 0.14.1 to rebuild the packaged converter.' }
$silkArchive = Join-Path $PSScriptRoot 'sources\silk-sdk.zip'
$archive = Join-Path $PSScriptRoot 'sources\lame-3.100.tar.gz'
if ((Get-FileHash -LiteralPath $silkArchive -Algorithm SHA256).Hash -ne '6CEC273D6E658A48A9F150BC43408CF8B79C26121EAF6BAEC569CC8CC6F2684E') { throw 'SILK source archive hash does not match.' }
if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne 'DDFE36CAB873794038AE2C1210557AD34857A4B6BDC515785D1DA9E175B1DA1E') { throw 'LAME source archive hash does not match.' }
New-Item -ItemType Directory -Path $stage | Out-Null
Expand-Archive -LiteralPath $silkArchive -DestinationPath $stage
& tar.exe -xf $archive -C $stage
if ($LASTEXITCODE -ne 0) { throw 'Unable to extract LAME sources.' }
Push-Location -LiteralPath $stage
try {
 $silkFiles = @(Get-ChildItem -LiteralPath 'silk\src' -File -Filter '*.c' | ForEach-Object { Join-Path 'silk\src' $_.Name })
 $lameFiles = @(Get-ChildItem -LiteralPath 'lame-3.100\libmp3lame' -File -Filter '*.c' | ForEach-Object { Join-Path 'lame-3.100\libmp3lame' $_.Name })
 & $Zig cc -target x86-windows.win7-gnu -municode -O2 -DNDEBUG -DHAVE_CONFIG_H=1 -ffunction-sections -fdata-sections '-Wno-shift-negative-value' '-Wno-tautological-pointer-compare' "-I$PSScriptRoot" '-Isilk/src' '-Isilk/interface' '-Ilame-3.100/include' '-Ilame-3.100/libmp3lame' @silkFiles @lameFiles (Join-Path $PSScriptRoot 'silk-to-mp3.c') '-Wl,--gc-sections' -s -o $Output
 if ($LASTEXITCODE -ne 0) { throw 'Unable to build the voice converter.' }
} finally { Pop-Location }
Write-Output "Voice converter: $Output"
