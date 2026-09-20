param(
    [ValidateSet('android-arm64', 'android-x64')][string]$RuntimeIdentifier = 'android-arm64',
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [string]$Dotnet = 'dotnet',
    [string]$AndroidSdkDirectory = $env:ANDROID_HOME,
    [string]$JavaSdkDirectory = $env:JAVA_HOME,
    [string]$KeyStore,
    [string]$PasswordFile,
    [string]$KeyAlias = 'slickwatch'
)
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
$previousResolver = $env:MSBuildEnableWorkloadResolver
$keyPasswordPath = $null
try {
    $env:MSBuildEnableWorkloadResolver = 'true'
    $arguments = @('publish', 'SlickWatch.Android', '-c', $Configuration, '-r', $RuntimeIdentifier,
        '-p:MSBuildEnableWorkloadResolver=true', '-o', "artifacts/$RuntimeIdentifier")
    if ($AndroidSdkDirectory) { $arguments += "-p:AndroidSdkDirectory=$AndroidSdkDirectory" }
    if ($JavaSdkDirectory) { $arguments += "-p:JavaSdkDirectory=$JavaSdkDirectory" }
    if ($KeyStore) {
        if (!$PasswordFile -or !(Test-Path -LiteralPath $PasswordFile)) { throw 'A password file is required for release signing.' }
        $keyPath = (Resolve-Path -LiteralPath $KeyStore).Path
        $passwordPath = (Resolve-Path -LiteralPath $PasswordFile).Path
        # apksigner consumes a line per password when both options share one file.
        # Separate temporary files also keep passwords out of process arguments.
        New-Item -ItemType Directory -Force .local/android-signing | Out-Null
        $keyPasswordPath = Join-Path $PWD ".local/android-signing/key-password-$([Guid]::NewGuid().ToString('N')).txt"
        Copy-Item -LiteralPath $passwordPath -Destination $keyPasswordPath
        $arguments += @('-p:AndroidKeyStore=true', "-p:AndroidSigningKeyStore=$keyPath", "-p:AndroidSigningKeyAlias=$KeyAlias",
            "-p:AndroidSigningStorePass=file:$passwordPath", "-p:AndroidSigningKeyPass=file:$keyPasswordPath")
    } elseif ($Configuration -eq 'Release') { throw 'Release builds require a signing key and password file. Use Debug for local development.' }
    & $Dotnet @arguments
    if ($LASTEXITCODE -ne 0) { throw 'Android build failed.' }
    $apk = Get-ChildItem -LiteralPath "artifacts/$RuntimeIdentifier" -Filter '*-Signed.apk' | Select-Object -First 1
    if (!$apk) { throw 'Signed APK was not produced.' }
    Copy-Item -LiteralPath $apk.FullName -Destination "artifacts/SlickWatch-$RuntimeIdentifier.apk" -Force
    Write-Output "APK: artifacts/SlickWatch-$RuntimeIdentifier.apk"
} finally {
    if ($keyPasswordPath -and (Test-Path -LiteralPath $keyPasswordPath)) { Remove-Item -LiteralPath $keyPasswordPath }
    $env:MSBuildEnableWorkloadResolver = $previousResolver
    Pop-Location
}
