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
    [string] $SecretManagerModulePath,

    [Parameter(Mandatory=$true)]
    [ValidateNotNullOrEmpty()]
    [string] $AzVaultName,

    [Parameter(Mandatory=$true)]
    [ValidateNotNullOrEmpty()]
    [string] $ExtensionVaultName,

    [Parameter(Mandatory=$true)]
    [ValidateNotNullOrEmpty()]
    [string] $SubscriptionId,

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
$extensionVaultModulePath = ((Get-Module -Name Az.KeyVault -ListAvailable )[0] | select Path).Path

# Import Secrets Management module
Import-Module -Name $SecretManagerModulePath -Force

# Register extension vault template
$RegisterAzSecretsVaultTemplate = @'
    Register-SecretsVault -Name {0} `
        -ModulePath '{1}' `
        -GetSecretScript {{
            param ([string] $Name,
                   [string] $VaultName,
                   [string] $SubscriptionId
            )

            Import-Module -Name Az.KeyVault -Force
            Import-Module -Name Az.Accounts -Force

            # $VerbosePreference = "Continue"

            Write-Verbose "Checking for Azure subscription"
            $azContext = Az.Accounts\Get-AzContext
            if (! $azContext -or ($azContext.Subscription.Id -ne $SubscriptionId))
            {{
                Write-Warning "Log into Azure account for Subscription: $SubscriptionId"
                Az.Accounts\Connect-AzAccount -Subscription $SubscriptionId
            }}

            if ([string]::IsNullOrEmpty($Name))
            {{
                $Name = "*"
            }}

            # Return all secrets that match Name pattern
            if ([WildcardPattern]::ContainsWildcardCharacters($Name))
            {{
                $pattern = [WildcardPattern]::new($Name)
                $vaultSecretInfos = Az.KeyVault\Get-AzKeyVaultSecret -VaultName $VaultName
                foreach ($vaultSecretInfo in $vaultSecretInfos)
                {{
                    if ($pattern.IsMatch($vaultSecretInfo.Name))
                    {{
                        $secret = Az.KeyVault\Get-AzKeyVaultSecret -VaultName $VaultName -Name $vaultSecretInfo.Name
                        Write-Output ([pscustomobject] @{{
                            Name = $secret.Name
                            Value = $secret.SecretValue
                        }})
                    }}
                }}

                return
            }}

            # Return single Name match value
            $secret = Az.KeyVault\Get-AzKeyVaultSecret -VaultName $VaultName -Name $Name
            if ($secret -ne $null)
            {{
                Write-Output ([pscustomobject] @{{
                    Name = $secret.Name
                    Value = $secret.SecretValue
                    Vault = $VaultName
                }})
            }}
        }} -GetSecretParameters @{{
            VaultName = '{2}'
            SubscriptionId = '{3}'
        }} -SetSecretScript {{
            param ([string] $Name,
                   [object] $SecretToWrite,
                   [string] $VaultName,
                   [string] $SubscriptionId
            )

            if (! ($SecretToWrite -is [securestring]))
            {{
                throw "AzKeyVault only supports SecureString secret data types."
            }}

            Import-Module -Name Az.Accounts -Force
            Import-Module -Name Az.KeyVault -Force

            # $VerbosePreference = "Continue"

            Write-Verbose "Checking for Azure subscription"
            $azContext = Az.Accounts\Get-AzContext
            if (! $azContext -or ($azContext.Subscription.Id -ne $SubscriptionId))
            {{
                Write-Warning "Log into Azure account for Subscription: $SubscriptionId"
                Az.Accounts\Connect-AzAccount -Subscription $SubscriptionId
            }}

            Az.KeyVault\Set-AzKeyVaultSecret -VaultName $VaultName -Name $Name -SecretValue $SecretToWrite
        }} -SetSecretParameters @{{
            VaultName = '{2}'
            SubscriptionId = '{3}'
        }} -RemoveSecretScript {{
            param ([string] $Name,
                   [string] $VaultName,
                   [string] $SubscriptionId
            )

            Import-Module -Name Az.Accounts -Force
            Import-Module -Name Az.KeyVault -Force

            # $VerbosePreference = "Continue"

            Write-Verbose "Checking for Azure subscription"
            $azContext = Az.Accounts\Get-AzContext
            if (! $azContext -or ($azContext.Subscription.Id -ne $SubscriptionId))
            {{
                Write-Warning "Log into Azure account for Subscription: $SubscriptionId"
                Az.Accounts\Connect-AzAccount -Subscription $SubscriptionId
            }}

            Az.KeyVault\Remove-AzKeyVaultSecret -VaultName $VaultName -Name $Name -Force
        }} -RemoveSecretParameters @{{
            VaultName = '{2}'
            SubscriptionId = '{3}'
        }}
'@

$RegisterAzSecretsVaultScript = $RegisterAzSecretsVaultTemplate -f $ExtensionVaultName, $extensionVaultModulePath, $AzVaultName, $SubscriptionId

Write-Output "Registering AZ vault: $ExtensionVaultName"
$sb = [scriptblock]::Create($RegisterAzSecretsVaultScript)
$sb.Invoke()
