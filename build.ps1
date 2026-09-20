param([string]$OutputDirectory = (Join-Path $PSScriptRoot 'app'))
$ErrorActionPreference = 'Stop'
$previousResolver = $env:MSBuildEnableWorkloadResolver
$env:MSBuildEnableWorkloadResolver = 'false'
Push-Location $PSScriptRoot
try {
    dotnet run --project SlickWatch.Tests/SlickWatch.Tests.csproj -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Behavior checks failed.' }
    dotnet publish SlickWatch.App/SlickWatch.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=embedded -o $OutputDirectory
    if ($LASTEXITCODE -ne 0) { throw 'Publishing failed.' }
    Write-Host "Ready: $(Join-Path $OutputDirectory 'SlickWatch.exe')"
}
finally {
    Pop-Location
    $env:MSBuildEnableWorkloadResolver = $previousResolver
}
