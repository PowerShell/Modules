#
# Script to install:
#   Azure KeyVault module
#   Microsoft.PowerShell.SecretsManagement (prototype) module
# and register an Azure vault extention to the PowerShell Secrets Manager
#

[CmdletBinding()]
param (
    [Parameter(Mandatory=$true)]
    [ValidateNotNullOrEmpty()]
    [string] $VaultRegistrationName,

    [Parameter(Mandatory=$true)]
    [ValidateNotNullOrEmpty()]
    [string] $AzVaultModulePathToRegister,

    [Parameter(Mandatory=$true)]
    [ValidateNotNullOrEmpty()]
    [string] $SecretManagerModulePath,

    [Parameter(Mandatory=$true)]
    [ValidateNotNullOrEmpty()]
    [string] $AzureResourceVaultName,

    [Parameter(Mandatory=$true)]
    [ValidateNotNullOrEmpty()]
    [string] $AzureSubscriptionId,

    [switch] $Force
)

function CheckModuleInstallation
{
    param (
        [string] $ModuleName,
        [switch] $Force
    )

    if ((Get-Module -Name $ModuleName -ListAvailable) -eq $null)
    {
        $shouldContinue = $Force
        if (! $shouldContinue)
        {
            $shouldContinue = $PSCmdlet.ShouldContinue("The required $ModuleName module is not installed.  Install it now?", "Register-AzureVaultExtension")
        }

        if ($shouldContinue)
        {
            Install-Module -Name $ModuleName -Force
        }
    }

    if ((Get-Module -Name $ModuleName -ListAvailable) -eq $null)
    {
        throw "Unable to find the $ModuleName module.  $ModuleName module is needed to register and use Azure vaults."
    }
}

# Ensure Az.Accounts module is installed
CheckModuleInstallation Az.Accounts $Force

# Ensure Az.KeyVault extension module is installed
CheckModuleInstallation Az.KeyVault $Force
$extensionVaultModulePath = ((Get-Module -Name Az.KeyVault -ListAvailable )[0] | Select-Object Path).Path

# Import Secrets Management module
Import-Module -Name $SecretManagerModulePath -Force

# Register extension vault template
$RegisterAzSecretsVaultTemplate = @'
    Register-SecretsVault -Name {0} `
        -ModulePath '{1}' `
        -VaultParameters @{{
            AZKVaultName = '{2}'
            SubscriptionId = '{3}'
        }}
'@

$RegisterAzSecretsVaultScript = $RegisterAzSecretsVaultTemplate -f $VaultRegistrationName, $AzVaultModulePathToRegister, `
    $AzureResourceVaultName, $AzureSubscriptionId

Write-Output "Registering AZ vault: $VaultRegistrationName"
$sb = [scriptblock]::Create($RegisterAzSecretsVaultScript)
$sb.Invoke()
