#
# Script to install SecretsManagement prototype module
#

[CmdletBinding()]
param (
    [Parameter(Mandatory=$true)]
    [ValidateNotNullOrEmpty()]
    [string] $ModuleInstallPath,

    [switch] $Force
)

$fullInstallPath = Join-Path (Resolve-Path -Path $ModuleInstallPath) "Microsoft.PowerShell.SecretsManagement"

if (! (Test-Path -Path $fullInstallPath))
{
    [System.IO.Directory]::CreateDirectory($fullInstallPath)
}
else
{
    if (! $Force)
    {
        throw "Module path already exists.  Use -Force to install over existing."
    }

    Remove-Item -Path (Join-Path $fullInstallPath '*') -Recurse -Force
}

$sourcePath = "\\scratch2\scratch\paulhi\Modules\Microsoft.PowerShell.SecretsManagement"
Copy-Item -Path (Join-Path $sourcePath "en-us") -Dest $fullInstallPath -Recurse
Copy-Item -Path (Join-Path $sourcePath "Microsoft.PowerShell.SecretsManagement.dll") -Dest $fullInstallPath
Copy-Item -Path (Join-Path $sourcePath "Microsoft.PowerShell.SecretsManagement.pdb") -Dest $fullInstallPath
Copy-Item -Path (Join-Path $sourcePath "Microsoft.PowerShell.SecretsManagement.psd1") -Dest $fullInstallPath
Copy-Item -Path (Join-Path $sourcePath "Microsoft.PowerShell.SecretsManagement.format.ps1xml") -Dest $fullInstallPath
