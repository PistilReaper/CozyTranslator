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
$releasePath = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot $release))
$artifactsPath = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'artifacts'))
if ((Split-Path -Parent $releasePath) -ne $artifactsPath) { throw 'Publish output must be directly inside artifacts.' }
if (Test-Path -LiteralPath $releasePath) { Remove-Item -LiteralPath $releasePath -Recurse -Force }
& $dotnet publish src/CozyTranslator.App -c Release -r win-x64 --self-contained true -o $release
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
Copy-Item -LiteralPath 'README.md','README.zh-CN.md','THIRD-PARTY.md','LICENSE' -Destination $release -Force
$releaseDocs = Join-Path $release 'docs'
New-Item -ItemType Directory -Path $releaseDocs -Force | Out-Null
Copy-Item -LiteralPath 'docs/screenshots' -Destination $releaseDocs -Recurse -Force
Copy-Item -LiteralPath "docs/RELEASE-v$version.md" -Destination $releaseDocs -Force
$sdkRoot = Split-Path -Parent $dotnet
foreach ($notice in @('LICENSE.txt','ThirdPartyNotices.txt')) {
    $path = Join-Path $sdkRoot $notice
    if (Test-Path -LiteralPath $path) { Copy-Item -LiteralPath $path -Destination $release -Force }
}
Compress-Archive -Path $release -DestinationPath $zip -Force
Get-FileHash -LiteralPath $zip -Algorithm SHA256
