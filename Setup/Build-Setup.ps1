#Requires -Version 5.1
<#
.SYNOPSIS
  Publishes the POS app and builds the Windows installer (Inno Setup).

.EXAMPLE
  .\Setup\Build-Setup.ps1
#>
$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
if (-not (Test-Path (Join-Path $Root "FruitVegetableMarketPOS.csproj"))) {
    $Root = $PSScriptRoot
    if (-not (Test-Path (Join-Path $Root "FruitVegetableMarketPOS.csproj"))) {
        throw "Run this script from the Stock Management project folder."
    }
}

$PublishDir = Join-Path $Root "bin\Release\net8.0-windows\win-x64\publish"
$IssPath = Join-Path $Root "Setup\Installer.iss"
$IsccCandidates = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
)
$Iscc = $IsccCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $Iscc) {
    throw "Inno Setup 6 not found. Install from https://jrsoftware.org/isinfo.php then re-run."
}

Write-Host "==> Publishing self-contained win-x64 Release..." -ForegroundColor Cyan
Push-Location $Root
try {
    dotnet publish "FruitVegetableMarketPOS.csproj" `
        -c Release `
        -r win-x64 `
        --self-contained true `
        -p:PublishSingleFile=false `
        -o $PublishDir
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }
}
finally {
    Pop-Location
}

if (-not (Test-Path (Join-Path $PublishDir "FruitVegetableMarketPOS.exe"))) {
    throw "Publish output missing FruitVegetableMarketPOS.exe at $PublishDir"
}

Write-Host "==> Compiling installer with Inno Setup..." -ForegroundColor Cyan
& $Iscc $IssPath
if ($LASTEXITCODE -ne 0) { throw "ISCC failed ($LASTEXITCODE)" }

$ReleaseDir = Join-Path $Root "Setup\Releases"
$Setup = Get-ChildItem $ReleaseDir -Filter "PMC_POS_Setup_*.exe" |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

if (-not $Setup) {
    throw "Installer EXE not found in $ReleaseDir"
}

Write-Host ""
Write-Host "Setup ready:" -ForegroundColor Green
Write-Host "  $($Setup.FullName)"
Write-Host "  Size: $([math]::Round($Setup.Length / 1MB, 1)) MB"
Write-Host ""
Write-Host "Install on any Windows 10/11 x64 PC (no .NET install required)." -ForegroundColor Green
Write-Host "Desktop shortcut is offered during setup. Default login: admin / admin123"
