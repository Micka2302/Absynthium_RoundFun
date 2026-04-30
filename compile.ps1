#!/usr/bin/env pwsh
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptRoot
Set-Location $scriptRoot

$project = Join-Path $scriptRoot 'Absynthium_RoundFun.csproj'
if (-not (Test-Path $project)) {
    throw "Project file not found at $project"
}

[xml]$projectXml = Get-Content -Raw $project

function Get-ProjectPropertyValue {
    param (
        [Parameter(Mandatory = $true)][xml]$ProjectXml,
        [Parameter(Mandatory = $true)][string]$PropertyName
    )

    $node = $ProjectXml.SelectSingleNode("//Project/PropertyGroup/$PropertyName")
    if ($node -and $node.InnerText) {
        return [string]$node.InnerText
    }

    return ''
}

$assemblyName = Get-ProjectPropertyValue -ProjectXml $projectXml -PropertyName 'AssemblyName'
if ([string]::IsNullOrWhiteSpace($assemblyName)) {
    $assemblyName = [System.IO.Path]::GetFileNameWithoutExtension($project)
}

$targetFramework = Get-ProjectPropertyValue -ProjectXml $projectXml -PropertyName 'TargetFramework'
if ([string]::IsNullOrWhiteSpace($targetFramework)) {
    throw "TargetFramework not found in $project"
}

$buildOutput = Join-Path $scriptRoot "bin/Release/$targetFramework"
$compiledRoot = Join-Path $repoRoot 'compiled'
$pluginTarget = Join-Path $compiledRoot "counterstrikesharp/plugins/$assemblyName"

Remove-Item -Recurse -Force $compiledRoot -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $pluginTarget -Force | Out-Null

dotnet restore $project
dotnet build $project -c Release --no-restore --nologo

if (-not (Test-Path $buildOutput)) {
    throw "Build output not found at $buildOutput"
}

Copy-Item -Path (Join-Path $buildOutput '*') -Destination $pluginTarget -Recurse -Force

$cssApi = Join-Path $pluginTarget 'CounterStrikeSharp.API.dll'
if (Test-Path $cssApi) {
    Remove-Item $cssApi -Force
}

$zipPath = Join-Path $compiledRoot "$assemblyName.zip"
if (Test-Path $zipPath) {
    Remove-Item $zipPath -Force
}

Compress-Archive -Path (Join-Path $pluginTarget '*') -DestinationPath $zipPath

Write-Host "[OK] Build finished."
Write-Host " - Project: $project"
Write-Host " - Folder:  $pluginTarget"
Write-Host " - Zip:     $zipPath"
