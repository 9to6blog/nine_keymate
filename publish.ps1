param([string]$OutputDirectory = (Join-Path $PSScriptRoot 'artifacts/win-x64'))
$ErrorActionPreference = 'Stop'
$destination = [System.IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $destination | Out-Null
dotnet publish (Join-Path $PSScriptRoot 'src/KeyMate.App/KeyMate.App.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:RestoreLockedMode=true -p:DebugType=None -p:DebugSymbols=false -o $destination
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README.md') -Destination (Join-Path $destination '사용안내.md')
$exe = Join-Path $destination 'KeyMate.exe'
$hash = Get-FileHash -LiteralPath $exe -Algorithm SHA256
Set-Content -LiteralPath (Join-Path $destination 'SHA256.txt') -Value "$($hash.Hash.ToLowerInvariant())  KeyMate.exe" -Encoding utf8
Write-Output "Published: $exe"
