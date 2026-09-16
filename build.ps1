$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
$localSdk = Join-Path $PSScriptRoot '.tools/dotnet/dotnet.exe'
$dotnet = if (Test-Path -LiteralPath $localSdk) { $localSdk } else { (Get-Command dotnet -ErrorAction Stop).Source }
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
if (Test-Path -LiteralPath $localSdk) { $env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot '.tools/cli' }
$version = ([xml](Get-Content -LiteralPath 'Directory.Build.props' -Raw)).Project.PropertyGroup.Version
$release = "artifacts/CozyTranslator-v$version-win-x64"
$zip = "$release.zip"
& $dotnet run --project tests/CozyTranslator.Tests -c Release
if ($LASTEXITCODE -ne 0) { throw 'Core verification failed.' }
& $dotnet run --project tests/CozyTranslator.WindowsTests -c Release -- artifacts/qa
if ($LASTEXITCODE -ne 0) { throw 'Windows verification failed.' }
& $dotnet publish src/CozyTranslator.App -c Release -r win-x64 --self-contained true -o $release
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
Copy-Item -LiteralPath 'README.md','VALIDATION.md','THIRD-PARTY.md','LICENSE' -Destination $release -Force
Copy-Item -LiteralPath 'docs' -Destination $release -Recurse -Force
$sdkRoot = Split-Path -Parent $dotnet
foreach ($notice in @('LICENSE.txt','ThirdPartyNotices.txt')) {
    $path = Join-Path $sdkRoot $notice
    if (Test-Path -LiteralPath $path) { Copy-Item -LiteralPath $path -Destination $release -Force }
}
Compress-Archive -Path $release -DestinationPath $zip -Force
Get-FileHash -LiteralPath $zip -Algorithm SHA256
