[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$')]
    [string] $Version,

    [switch] $SkipInstaller,

    [string] $CertificatePath = $env:WARMUSIC_SIGNING_CERTIFICATE_PATH,

    [string] $CertificatePassword = $env:WARMUSIC_SIGNING_CERTIFICATE_PASSWORD,

    [string] $WixEulaId = $env:WARMUSIC_WIX_EULA_ID,

    [string] $TimestampUrl = 'https://timestamp.digicert.com'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = Split-Path -Parent $PSScriptRoot
$artifacts = Join-Path $root 'artifacts'
$staging = Join-Path $artifacts 'staging'
$installed = Join-Path $staging 'installed'
$portable = Join-Path $staging 'portable'
$applicationProject = Join-Path $root 'src/WarMusic/WarMusic.csproj'
$installerProject = Join-Path $root 'packaging/WarMusic.Installer/WarMusic.Installer.wixproj'
$executableArtifact = Join-Path $artifacts "WarMusic-$Version-win-x64.exe"
$portableArchive = Join-Path $artifacts "WarMusic-$Version-win-x64-portable.zip"
$installerArtifact = Join-Path $artifacts "WarMusic-$Version-win-x64-setup.msi"
$checksumFile = Join-Path $artifacts 'SHA256SUMS.txt'
$provenanceFile = Join-Path $artifacts 'provenance.json'

if (Test-Path $staging) {
    Remove-Item $staging -Recurse -Force
}
New-Item $installed -ItemType Directory -Force | Out-Null
New-Item $portable -ItemType Directory -Force | Out-Null

dotnet restore $applicationProject --locked-mode -r win-x64
if ($LASTEXITCODE -ne 0) {
    throw 'Locked application restore failed.'
}

dotnet publish $applicationProject -c Release -r win-x64 --self-contained true --no-restore -o $installed -p:Version=$Version
if ($LASTEXITCODE -ne 0) {
    throw 'Application publish failed.'
}

$publishedFiles = @(Get-ChildItem $installed -File -Recurse)
if ($publishedFiles.Count -ne 1 -or $publishedFiles[0].Name -ne 'WarMusic.exe') {
    $names = ($publishedFiles | ForEach-Object { [System.IO.Path]::GetRelativePath($installed, $_.FullName) }) -join ', '
    throw "Single-file publish produced unexpected files: $names"
}

Copy-Item (Join-Path $root 'src/WarMusic/Assets/WarMusic.ico') (Join-Path $installed 'WarMusic.ico')

$documentation = Join-Path $installed 'docs'
New-Item $documentation -ItemType Directory -Force | Out-Null
Copy-Item (Join-Path $root 'docs/*') $documentation -Recurse
Copy-Item (Join-Path $root 'README.md') $installed
$dotnetRoot = Split-Path -Parent (Get-Command dotnet).Source
$dotnetLicense = Join-Path $dotnetRoot 'LICENSE.txt'
$dotnetNotices = Join-Path $dotnetRoot 'ThirdPartyNotices.txt'
if (-not (Test-Path $dotnetLicense) -or -not (Test-Path $dotnetNotices)) {
    throw 'The .NET license or third-party notices could not be found beside the selected SDK.'
}
Copy-Item $dotnetLicense (Join-Path $documentation 'DOTNET-LICENSE.txt')
Copy-Item $dotnetNotices (Join-Path $documentation 'DOTNET-THIRD-PARTY-NOTICES.txt')

function Find-SignTool {
    $command = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    $kits = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits/10/bin'
    if (Test-Path $kits) {
        $candidate = Get-ChildItem $kits -Filter signtool.exe -Recurse |
            Where-Object FullName -Match '\\x64\\signtool\.exe$' |
            Sort-Object FullName -Descending |
            Select-Object -First 1
        if ($candidate) {
            return $candidate.FullName
        }
    }

    throw 'Signing was requested, but signtool.exe was not found.'
}

function Invoke-CodeSign([string] $Path) {
    if ([string]::IsNullOrWhiteSpace($CertificatePath)) {
        return
    }

    if (-not (Test-Path $CertificatePath)) {
        throw "Signing certificate not found: $CertificatePath"
    }

    $signTool = Find-SignTool
    $arguments = @('sign', '/fd', 'SHA256', '/td', 'SHA256', '/tr', $TimestampUrl, '/f', $CertificatePath)
    if (-not [string]::IsNullOrWhiteSpace($CertificatePassword)) {
        $arguments += @('/p', $CertificatePassword)
    }
    $arguments += $Path
    & $signTool @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Code signing failed: $Path"
    }
}

Invoke-CodeSign (Join-Path $installed 'WarMusic.exe')
Copy-Item (Join-Path $installed 'WarMusic.exe') $executableArtifact -Force

if ([string]::IsNullOrWhiteSpace($CertificatePath)) {
    Set-Content (Join-Path $installed 'UNSIGNED-BUILD.txt') 'This build is unsigned. Verify SHA256SUMS.txt before running it.' -Encoding utf8NoBOM
}

Copy-Item (Join-Path $installed '*') $portable -Recurse
Copy-Item (Join-Path $root 'packaging/WarMusic.portable') (Join-Path $portable 'WarMusic.portable')

Add-Type -AssemblyName System.IO.Compression
if (Test-Path $portableArchive) {
    Remove-Item $portableArchive -Force
}
$zip = [System.IO.Compression.ZipFile]::Open($portableArchive, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    Get-ChildItem $portable -File -Recurse | Sort-Object FullName | ForEach-Object {
        $relative = [System.IO.Path]::GetRelativePath($portable, $_.FullName).Replace('\', '/')
        $entry = $zip.CreateEntry($relative, [System.IO.Compression.CompressionLevel]::Optimal)
        # ZIP timestamps cannot precede 1980. A fixed timestamp also keeps the
        # archive byte-for-byte reproducible for identical inputs.
        $entry.LastWriteTime = [DateTimeOffset]::new(1980, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
        $source = $_.OpenRead()
        $destination = $entry.Open()
        try {
            $source.CopyTo($destination)
        }
        finally {
            $destination.Dispose()
            $source.Dispose()
        }
    }
}
finally {
    $zip.Dispose()
}

$releaseFiles = @($executableArtifact, $portableArchive)
if (-not $SkipInstaller) {
    if ([string]::IsNullOrWhiteSpace($WixEulaId)) {
        throw 'MSI creation requires explicit WiX 7 EULA acceptance. Set WARMUSIC_WIX_EULA_ID=wix7 after reviewing https://docs.firegiant.com/wix/osmf/.'
    }

    dotnet restore $installerProject --locked-mode
    if ($LASTEXITCODE -ne 0) {
        throw 'Locked installer restore failed.'
    }

    $installerOutput = Join-Path $staging 'installer'
    New-Item $installerOutput -ItemType Directory -Force | Out-Null
    $numericVersion = ([regex]::Match($Version, '^\d+\.\d+\.\d+')).Value
    dotnet build $installerProject -c Release --no-restore -p:PublishDir=$installed -p:ProductVersion=$numericVersion -p:OutputPath=$installerOutput -p:WixEulaId=$WixEulaId
    if ($LASTEXITCODE -ne 0) {
        throw 'Installer build failed.'
    }

    $builtInstaller = Get-ChildItem $installerOutput -Filter '*.msi' -Recurse | Select-Object -First 1
    if (-not $builtInstaller) {
        throw 'Installer build completed without producing an MSI.'
    }
    Copy-Item $builtInstaller.FullName $installerArtifact -Force
    Invoke-CodeSign $installerArtifact
    $releaseFiles += $installerArtifact
}

$checksumLines = foreach ($file in $releaseFiles | Sort-Object) {
    $hash = (Get-FileHash $file -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $(Split-Path $file -Leaf)"
}
Set-Content $checksumFile $checksumLines -Encoding ascii

$sourceCommit = $env:GITHUB_SHA
if ([string]::IsNullOrWhiteSpace($sourceCommit)) {
    $sourceCommit = (& git -C $root rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0) {
        throw 'Unable to determine the source commit for provenance.'
    }
}

$sourceDirty = -not [string]::IsNullOrWhiteSpace(
    ((& git -C $root status --porcelain --untracked-files=normal) -join "`n"))
if ($LASTEXITCODE -ne 0) {
    throw 'Unable to determine the working-tree state for provenance.'
}

$subjects = foreach ($file in $releaseFiles | Sort-Object) {
    [ordered]@{
        name = Split-Path $file -Leaf
        sha256 = (Get-FileHash $file -Algorithm SHA256).Hash.ToLowerInvariant()
    }
}
$provenance = [ordered]@{
    schemaVersion = 1
    application = 'WarMusic'
    version = $Version
    source = [ordered]@{
        repository = if ($env:GITHUB_REPOSITORY) { $env:GITHUB_REPOSITORY } else { 'local' }
        commit = $sourceCommit
        dirty = $sourceDirty
    }
    build = [ordered]@{
        workflow = if ($env:GITHUB_WORKFLOW_REF) { $env:GITHUB_WORKFLOW_REF } else { 'local' }
        run = if ($env:GITHUB_RUN_ID) { $env:GITHUB_RUN_ID } else { 'local' }
        sdk = (dotnet --version).Trim()
        runtimeIdentifier = 'win-x64'
        selfContained = $true
        singleFile = $true
        signed = -not [string]::IsNullOrWhiteSpace($CertificatePath)
        createdUtc = [DateTimeOffset]::UtcNow.ToString('O')
    }
    subjects = @($subjects)
}
$provenance | ConvertTo-Json -Depth 6 | Set-Content $provenanceFile -Encoding utf8NoBOM

Write-Host "Created release artifacts in $artifacts"
Get-ChildItem $artifacts -File | Select-Object Name, Length
