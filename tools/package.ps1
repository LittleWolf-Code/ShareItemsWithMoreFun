# Compile le mod en Release et produit le zip Thunderstore dans dist\.
# Usage : powershell -ExecutionPolicy Bypass -File tools\package.ps1
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'ShareItemsWithMoreFun\ShareItemsWithMoreFun.csproj'
$thunderstore = Join-Path $root 'Thunderstore'
$manifest = Get-Content (Join-Path $thunderstore 'manifest.json') -Raw | ConvertFrom-Json

dotnet build $project -c Release -nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'La compilation a echoue.' }

$staging = Join-Path $root 'dist\staging'
if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
New-Item -ItemType Directory -Force $staging | Out-Null

Copy-Item (Join-Path $thunderstore 'manifest.json') $staging
Copy-Item (Join-Path $thunderstore 'icon.png') $staging
Copy-Item (Join-Path $thunderstore 'README.md') $staging
Copy-Item (Join-Path $thunderstore 'CHANGELOG.md') $staging
Copy-Item (Join-Path $root 'ShareItemsWithMoreFun\bin\Release\ShareItemsWithMoreFun.dll') $staging

$zip = Join-Path $root ("dist\{0}-{1}.zip" -f $manifest.name, $manifest.version_number)
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $staging '*') -DestinationPath $zip
Remove-Item $staging -Recurse -Force
"Paquet pret : $zip"
