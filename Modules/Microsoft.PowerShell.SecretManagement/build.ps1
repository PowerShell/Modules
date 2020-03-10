# Copyright (c) Microsoft Corporation. All rights reserved.
# Licensed under the MIT License.

param (
    [Parameter(ParameterSetName="build")]
    [switch]
    $Clean,

    [Parameter(ParameterSetName="build")]
    [switch]
    $Build,

    [Parameter(ParameterSetName="build")]
    [switch]
    $Test,

    [Parameter(ParameterSetName="build")]
    [string[]]
    [ValidateSet("Functional","StaticAnalysis")]
    $TestType = @("Functional"),

    [Parameter(ParameterSetName="help")]
    [switch]
    $UpdateHelp,

    [ValidateSet("Debug", "Release")]
    [string] $BuildConfiguration = "Debug",

    [ValidateSet("netstandard2.0")]
    [string] $BuildFramework = "netstandard2.0"
)

if ( ! ( Get-Module -ErrorAction SilentlyContinue PSPackageProject) ) {
    Install-Module PSPackageProject
}

$config = Get-PSPackageProjectConfiguration -ConfigPath $PSScriptRoot

$script:ModuleName = $config.ModuleName
$script:SrcPath = $config.SourcePath
$script:OutDirectory = $config.BuildOutputPath
$script:TestPath = $config.TestPath

$script:ModuleRoot = $PSScriptRoot
$script:Culture = $config.Culture
$script:HelpPath = $config.HelpPath

$script:BuildConfiguration = $BuildConfiguration
$script:BuildFramework = $BuildFramework

. "$PSScriptRoot\doBuild.ps1"

# The latest DotNet (3.1.1) is needed to perform binary build.
$dotNetCmd = Get-Command -Name dotNet -ErrorAction SilentlyContinue
$dotnetVersion = $null
if ($dotNetCmd -ne $null) {
    $info = dotnet --info
    foreach ($item in $info) {
        $index = $item.IndexOf('Version:')
        if ($index -gt -1) {
            $versionStr = $item.SubString('Version:'.Length + $index)
            $null = [version]::TryParse($versionStr, [ref] $dotnetVersion)
            break
        }
    }
}
# DotNet 3.1.1 is installed in ci.yml.  Just check installation and version here.
Write-Verbose -Verbose -Message "Installed DotNet found: $($dotNetCmd -ne $null), version: $versionStr"
<#
$dotNetVersionOk = ($dotnetVersion -ne $null) -and ((($dotnetVersion.Major -eq 3) -and ($dotnetVersion.Minor -ge 1)) -or ($dotnetVersion.Major -gt 3))
if (! $dotNetVersionOk) {
    
    Write-Verbose -Verbose -Message "Installing dotNet..."
    $installObtainUrl = "https://dotnet.microsoft.com/download/dotnet-core/scripts/v1"

    Remove-Item -ErrorAction SilentlyContinue -Recurse -Force ~\AppData\Local\Microsoft\dotnet
    $installScript = "dotnet-install.ps1"
    Invoke-WebRequest -Uri $installObtainUrl/$installScript -OutFile $installScript

    & ./$installScript -Channel 'release' -Version '3.1.101'
    Write-Verbose -Verbose -Message "dotNet installation complete."
}
#>

if ($Clean -and (Test-Path $OutDirectory))
{
    Remove-Item -Path $OutDirectory -Force -Recurse -ErrorAction Stop -Verbose

    if (Test-Path "${SrcPath}/code/bin")
    {
        Remove-Item -Path "${SrcPath}/code/bin" -Recurse -Force -ErrorAction Stop -Verbose
    }

    if (Test-Path "${SrcPath}/code/obj")
    {
        Remove-Item -Path "${SrcPath}/code/obj" -Recurse -Force -ErrorAction Stop -Verbose
    }
}

if (-not (Test-Path $OutDirectory))
{
    $script:OutModule = New-Item -ItemType Directory -Path (Join-Path $OutDirectory $ModuleName)
}
else
{
    $script:OutModule = Join-Path $OutDirectory $ModuleName
}

if ($Build.IsPresent)
{
    $sb = (Get-Item Function:DoBuild).ScriptBlock
    Invoke-PSPackageProjectBuild -BuildScript $sb
}

if ( $Test.IsPresent ) {
    Invoke-PSPackageProjectTest -Type $TestType
}

if ($UpdateHelp.IsPresent) {
    Add-PSPackageProjectCmdletHelp -ProjectRoot $ModuleRoot -ModuleName $ModuleName -Culture $Culture
}
