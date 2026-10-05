$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$version = (Get-Content -Raw (Join-Path $root "version.txt")).Trim()
if ($version -notmatch '^\d+\.\d+\.\d+$') {
    throw "version.txt must look like 1.2.3"
}
$out = Join-Path $root "publish"
if (Test-Path $out) {
    Remove-Item $out -Recurse -Force
}
$publishArgs = @("-c", "Release", "-r", "win-x64", "--self-contained", "true", "-p:DebugType=none", "-p:DebugSymbols=false", "-o", $out)
dotnet publish (Join-Path $root "src\OlympiadGate.Service\OlympiadGate.Service.csproj") @publishArgs
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
dotnet publish (Join-Path $root "src\OlympiadGate.Desktop\OlympiadGate.Desktop.csproj") @publishArgs
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$candidates = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
)
$iscc = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if ($iscc) {
    & $iscc "/DAppVersion=$version" (Join-Path $PSScriptRoot "OlympiadGate.iss")
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
} else {
    Write-Host "Inno Setup is not installed. Published files are in publish. Build the setup later with: ISCC installer\OlympiadGate.iss"
}
