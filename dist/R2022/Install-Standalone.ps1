$ErrorActionPreference = 'Stop'
$src = $PSScriptRoot
$dest = Join-Path $env:APPDATA "Autodesk\Revit\Addins\2022"
New-Item -ItemType Directory -Force -Path $dest | Out-Null
Copy-Item (Join-Path $src '*') $dest -Recurse -Force
Write-Host "Installed to $dest"
Write-Host "Restart Revit to load BCFier."
