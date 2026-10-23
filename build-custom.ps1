param(
    [ValidateSet('net10.0-windows', 'net6.0-windows')]
    [string]$Framework = 'net10.0-windows',
    [ValidateSet('win-x64', 'win-x86', 'win-arm64')]
    [string]$Runtime = 'win-x64',
    [switch]$FrameworkDependent,
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
if ($Framework -eq 'net6.0-windows' -and $Runtime -eq 'win-arm64') {
    throw 'The upstream Windows 7/8 target does not support ARM64.'
}

Push-Location $PSScriptRoot
try {
    if (-not $SkipTests) {
        & dotnet run --project tests/RetroBar.SmokeTests -c Release -f $Framework -- $PSScriptRoot
        if ($LASTEXITCODE -ne 0) { throw 'Smoke tests failed.' }
    }

    $packageName = "RetroBar-Shizuku-ThemedIcons-$Runtime-$Framework"
    $artifactsRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'artifacts'))
    $null = New-Item -ItemType Directory -Path $artifactsRoot -Force
    if ((Get-Item -LiteralPath $artifactsRoot).Attributes -band [System.IO.FileAttributes]::ReparsePoint) {
        throw 'The artifacts directory must not be a junction or symbolic link.'
    }
    $publishDir = Join-Path $artifactsRoot $packageName
    if (Test-Path -LiteralPath $publishDir) {
        $item = Get-Item -LiteralPath $publishDir
        $resolvedOutput = (Resolve-Path -LiteralPath $publishDir).Path
        if (-not $resolvedOutput.StartsWith($artifactsRoot + [System.IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or
            ($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint)) {
            throw 'Refusing to clean an output directory outside artifacts or through a link.'
        }
        Remove-Item -LiteralPath $resolvedOutput -Recurse -Force
    }

    $selfContained = (-not $FrameworkDependent.IsPresent).ToString().ToLowerInvariant()
    & dotnet publish RetroBar/RetroBar.csproj -c Release -f $Framework -r $Runtime `
        --self-contained $selfContained --nologo -o $publishDir `
        -p:PublishSingleFile=true -p:IncludeAllContentForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true -p:PublishTrimmed=false `
        -p:DebugType=None -p:DebugSymbols=false
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }

    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'DistLicense.txt') -Destination (Join-Path $publishDir 'License.txt')
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'CUSTOM-BUILD.md') -Destination $publishDir
    $exe = Join-Path $publishDir 'RetroBar.exe'
    $revision = & git rev-parse HEAD
    if ($LASTEXITCODE -ne 0) { throw 'Cannot read source revision.' }
    $dirty = [bool](& git status --porcelain)
    [ordered]@{
        name = $packageName
        sourceRevision = $revision
        sourceDirty = $dirty
        framework = $Framework
        runtime = $Runtime
        selfContained = -not $FrameworkDependent.IsPresent
        builtAtUtc = [DateTime]::UtcNow.ToString('o')
        executableSha256 = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash
    } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $publishDir 'build-info.json') -Encoding UTF8

    $zip = Join-Path $artifactsRoot "$packageName.zip"
    Compress-Archive -Path (Join-Path $publishDir '*') -DestinationPath $zip -Force
    Write-Host "Executable: $exe"
    Write-Host "Archive: $zip"
}
finally {
    Pop-Location
}
