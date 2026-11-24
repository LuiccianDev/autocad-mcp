<#
.SYNOPSIS
    Installs AutoCAD.Gateway as an Autodesk Autoloader .bundle with versioned Contents folders.
.DESCRIPTION
    Builds AutoCAD.Gateway (unless -NoBuild is passed), stages AutoCAD.Gateway.dll,
    AutoCAD.Gateway.Contracts.dll, AutoCAD.Gateway.deps.json, and supporting runtime binaries
    into Contents/<timestamp>/ to avoid file locks when AutoCAD is open, and atomically writes
    PackageContents.xml for AutoCAD 2027 and Civil 3D 2027 (R26.x / .NET 10).
.PARAMETER Configuration
    MSBuild configuration to build/stage ('Release' or 'Debug'). Defaults to 'Release'.
.PARAMETER NoBuild
    Skip running dotnet build and stage existing output assemblies directly.
.PARAMETER OutputDir
    Target .bundle directory. Defaults to '%APPDATA%\Autodesk\ApplicationPlugins\AutoCAD.Gateway.bundle'.
.PARAMETER PruneOld
    Remove older unlocked Contents/<timestamp> subdirectories after staging.
.PARAMETER Uninstall
    Remove the installed .bundle directory at -OutputDir when unlocked.
#>

[CmdletBinding()]
param(
    [ValidateSet("Release", "Debug")]
    [string]$Configuration = "Release",

    [switch]$NoBuild,

    [ValidateNotNullOrEmpty()]
    [string]$OutputDir = $(if ($env:APPDATA) {
        Join-Path $env:APPDATA "Autodesk\ApplicationPlugins\AutoCAD.Gateway.bundle"
    } else {
        Join-Path $HOME "Autodesk\ApplicationPlugins\AutoCAD.Gateway.bundle"
    }),

    [switch]$PruneOld,

    [switch]$Uninstall
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    throw "Parameter '-OutputDir' cannot be empty or whitespace."
}

$OutputDir = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputDir)

function Test-BundleUnlocked {
    param([Parameter(Mandatory = $true)][string]$BundlePath)

    if (-not (Test-Path $BundlePath)) {
        return $true
    }

    $lockedFiles = @()
    Get-ChildItem -Path $BundlePath -Filter "*.dll" -Recurse -File -ErrorAction SilentlyContinue | ForEach-Object {
        try {
            $fs = [System.IO.File]::Open($_.FullName, [System.IO.FileMode]::Open, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None)
            $fs.Dispose()
        } catch {
            $lockedFiles += $_.FullName
        }
    }

    return ($lockedFiles.Count -eq 0)
}

if ($Uninstall) {
    if (-not (Test-Path $OutputDir)) {
        Write-Host "[OK] AutoCAD.Gateway.bundle is not installed at: $OutputDir" -ForegroundColor Green
        exit 0
    }

    if (-not (Test-BundleUnlocked -BundlePath $OutputDir)) {
        throw "Cannot uninstall '$OutputDir' because one or more plugin DLLs are locked. Close AutoCAD 2027 first and retry."
    }

    Remove-Item -LiteralPath $OutputDir -Recurse -Force
    Write-Host "[OK] AutoCAD.Gateway.bundle uninstalled from: $OutputDir" -ForegroundColor Green
    exit 0
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$gatewayProject = Join-Path $repoRoot "AutoCAD.Gateway\AutoCAD.Gateway.csproj"

if (-not (Test-Path $gatewayProject)) {
    throw "Gateway project not found at: $gatewayProject"
}

if (-not $NoBuild) {
    Write-Host "Building AutoCAD.Gateway ($Configuration)..." -ForegroundColor Yellow
    dotnet build "$gatewayProject" -c $Configuration
    if ($LASTEXITCODE -ne 0) {
        throw "Build failed with exit code $LASTEXITCODE"
    }
}

$binDir = Join-Path $repoRoot "AutoCAD.Gateway\bin\$Configuration\net10.0-windows"
$gatewayDll = Join-Path $binDir "AutoCAD.Gateway.dll"
$contractsDll = Join-Path $binDir "AutoCAD.Gateway.Contracts.dll"

if (-not (Test-Path $gatewayDll)) {
    throw "Compiled assembly not found: $gatewayDll"
}
if (-not (Test-Path $contractsDll)) {
    throw "Contracts assembly not found: $contractsDll"
}

$versionInfo = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($gatewayDll)
$appVersion = if ($versionInfo.FileVersion) { $versionInfo.FileVersion } else { "2.5.0.0" }
$contentsRoot = Join-Path $OutputDir "Contents"

$versionTag = (Get-Date).ToUniversalTime().ToString("yyyyMMdd-HHmmss-fff")
$stageDir = Join-Path $contentsRoot $versionTag
$suffix = 1
while (Test-Path $stageDir) {
    $stageDir = Join-Path $contentsRoot "$versionTag-$suffix"
    $suffix++
}
$finalTag = Split-Path -Leaf $stageDir

New-Item -ItemType Directory -Path $stageDir -Force | Out-Null

$upgradeCode = "{A4E7B6D0-4A3F-4D8C-9F6A-202700000002}"
$productCode = "{" + ([Guid]::NewGuid().ToString().ToUpperInvariant()) + "}"

$manifestPath = Join-Path $OutputDir "PackageContents.xml"
$tmpManifestPath = Join-Path $OutputDir "PackageContents.xml.tmp"
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
$writerSettings = New-Object System.Xml.XmlWriterSettings
$writerSettings.Encoding = $utf8NoBom
$writerSettings.Indent = $true

$commandNames = @(
    "AI_START_SERVER",
    "AI_STOP_SERVER",
    "AI_SHOW_CONFIG",
    "AI_SET_TOKEN",
    "AI_GENERATE_TOKEN",
    "AI_EDIT_GUARD"
)

try {
    # Stage all compiled plugin DLLs, runtime dependency manifests, and PDB symbols cleanly.
    $srcHashes = @{}
    Get-ChildItem -Path $binDir -File | Where-Object {
        $_.Extension -in @(".dll", ".json", ".pdb") -and -not $_.Name.EndsWith(".runtimeconfig.json", [System.StringComparison]::OrdinalIgnoreCase)
    } | ForEach-Object {
        $srcHash = (Get-FileHash -Path $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        $destPath = Join-Path $stageDir $_.Name
        Copy-Item -Path $_.FullName -Destination $destPath -Force
        $destHash = (Get-FileHash -Path $destPath -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($srcHash -ne $destHash) {
            throw "SHA-256 verification failed for staged file '$($_.Name)'."
        }
        $srcHashes[$_.Name] = $destHash
    }

    $checksumPath = Join-Path $stageDir "checksums.sha256"
    $checksumLines = $srcHashes.Keys | Sort-Object | ForEach-Object { "$($srcHashes[$_])  $_" }
    [System.IO.File]::WriteAllLines($checksumPath, $checksumLines, $utf8NoBom)

    $writer = [System.Xml.XmlWriter]::Create($tmpManifestPath, $writerSettings)
    try {
        $writer.WriteStartDocument()
        $writer.WriteStartElement("ApplicationPackage")
        $writer.WriteAttributeString("SchemaVersion", "1.0")
        $writer.WriteAttributeString("AutodeskProduct", "AutoCAD")
        $writer.WriteAttributeString("ProductType", "Application")
        $writer.WriteAttributeString("Name", "AutoCAD AI Gateway")
        $writer.WriteAttributeString("Description", "Loopback HTTP Gateway and native ObjectARX bridge for AI coding agents.")
        $writer.WriteAttributeString("AppVersion", $appVersion)
        $writer.WriteAttributeString("Author", "Luiccian (@LuiccianDev)")
        $writer.WriteAttributeString("ProductCode", $productCode)
        $writer.WriteAttributeString("UpgradeCode", $upgradeCode)

        $writer.WriteStartElement("CompanyDetails")
        $writer.WriteAttributeString("Name", "LuiccianDev")
        $writer.WriteAttributeString("Url", "https://github.com/LuiccianDev/autocad-mcp")
        $writer.WriteEndElement()

        $writer.WriteStartElement("Components")
        $writer.WriteAttributeString("Description", "AutoCAD 2027 & Civil 3D 2027 (.NET 10 / R26.x) Managed Gateway")

        $writer.WriteStartElement("RuntimeRequirements")
        $writer.WriteAttributeString("OS", "Win64")
        $writer.WriteAttributeString("Platform", "AutoCAD*|Civil3D")
        $writer.WriteAttributeString("SeriesMin", "R26.0")
        $writer.WriteAttributeString("SeriesMax", "R26.9")
        $writer.WriteEndElement()

        $writer.WriteStartElement("ComponentEntry")
        $writer.WriteAttributeString("AppName", "AutoCAD.Gateway")
        $writer.WriteAttributeString("AppType", ".Net")
        $writer.WriteAttributeString("Version", $appVersion)
        $writer.WriteAttributeString("ModuleName", "./Contents/$finalTag/AutoCAD.Gateway.dll")
        $writer.WriteAttributeString("AppDescription", "AutoCAD AI Gateway")
        $writer.WriteAttributeString("LoadOnAutoCADStartup", "True")

        $writer.WriteStartElement("Commands")
        $writer.WriteAttributeString("GroupName", "AUTOCAD_AI_GATEWAY")
        foreach ($cmdName in $commandNames) {
            $writer.WriteStartElement("Command")
            $writer.WriteAttributeString("Local", $cmdName)
            $writer.WriteAttributeString("Global", $cmdName)
            $writer.WriteEndElement()
        }
        $writer.WriteEndElement() # Commands
        $writer.WriteEndElement() # ComponentEntry
        $writer.WriteEndElement() # Components
        $writer.WriteEndElement() # ApplicationPackage
        $writer.WriteEndDocument()
    } finally {
        $writer.Dispose()
    }
    Move-Item -LiteralPath $tmpManifestPath -Destination $manifestPath -Force
} catch {
    if (Test-Path $tmpManifestPath) {
        Remove-Item -LiteralPath $tmpManifestPath -Force -ErrorAction SilentlyContinue
    }
    if (Test-Path $stageDir) {
        Remove-Item -LiteralPath $stageDir -Recurse -Force -ErrorAction SilentlyContinue
    }
    throw
}

if ($PruneOld) {
    Get-ChildItem -Path $contentsRoot -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -ne $finalTag } |
        ForEach-Object {
            if (Test-BundleUnlocked -BundlePath $_.FullName) {
                Remove-Item -Path $_.FullName -Recurse -Force -ErrorAction SilentlyContinue
            }
        }
}

Write-Host "[OK] AutoCAD.Gateway.bundle staged at: $OutputDir" -ForegroundColor Green
Write-Host "  -> Manifest : $manifestPath" -ForegroundColor DarkGray
Write-Host "  -> Payload  : $stageDir" -ForegroundColor DarkGray
