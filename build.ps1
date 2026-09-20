param(
    [ValidateSet('win-x64', 'win-arm64', 'linux-x64', 'linux-arm64', 'osx-x64', 'osx-arm64')]
    [string]$RuntimeIdentifier = [System.Runtime.InteropServices.RuntimeInformation]::RuntimeIdentifier,
    [string]$OutputDirectory,
    [switch]$SkipTests
)
$ErrorActionPreference = 'Stop'
$previousResolver = $env:MSBuildEnableWorkloadResolver
$env:MSBuildEnableWorkloadResolver = 'false'
if (!$OutputDirectory) { $OutputDirectory = Join-Path $PSScriptRoot "artifacts/$RuntimeIdentifier" }
$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
Push-Location $PSScriptRoot
try {
    if (!$SkipTests) {
        dotnet run --project SlickWatch.Tests/SlickWatch.Tests.csproj -c Release
        if ($LASTEXITCODE -ne 0) { throw 'Behavior checks failed.' }
    }
    $publishDirectory = $OutputDirectory
    if ($RuntimeIdentifier.StartsWith('osx-')) { $publishDirectory = Join-Path $OutputDirectory 'SlickWatch.app/Contents/MacOS' }
    dotnet publish SlickWatch.App/SlickWatch.App.csproj -c Release -r $RuntimeIdentifier --self-contained true -p:DebugType=embedded -o $publishDirectory
    if ($LASTEXITCODE -ne 0) { throw 'Publishing failed.' }
    if ($RuntimeIdentifier.StartsWith('osx-')) {
        $contentsDirectory = Split-Path $publishDirectory
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'packaging/macos/Info.plist') -Destination (Join-Path $contentsDirectory 'Info.plist')
        if ([System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::OSX)) {
            & chmod +x (Join-Path $publishDirectory 'SlickWatch')
            & codesign --force --deep --sign - (Join-Path $OutputDirectory 'SlickWatch.app')
            if ($LASTEXITCODE -ne 0) { throw 'Local ad-hoc macOS signing failed.' }
        }
    }
    elseif ($RuntimeIdentifier.StartsWith('linux-') -and [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Linux)) {
        & chmod +x (Join-Path $publishDirectory 'SlickWatch')
    }
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Readme.md') -Destination $OutputDirectory
    Write-Host "Ready: $OutputDirectory"
}
finally { Pop-Location; $env:MSBuildEnableWorkloadResolver = $previousResolver }
