# Copyright (c) Microsoft Corporation. All rights reserved.
# Licensed under the MIT License.

function Get-Path
{
    $path = Join-Path $env:TEMP 'TestVault18'
    if (! (Test-Path -Path $path))
    {
        [System.IO.Directory]::CreateDirectory($path)
    }

    return $path
}

function Get-Secret
{
    param (
        [string] $Name,
        [string] $VaultName,
        [hashtable] $AdditionalParameters
    )

    if ([WildcardPattern]::ContainsWildcardCharacters($Name))
    {
        throw "The Name parameter cannot contain wild card characters."
    }

    $filePath = Join-Path -Path (Get-Path) -ChildPath "${Name}.xml"
    if (! (Test-Path -Path $filePath))
    {
        return
    }

    $secret = Import-Clixml -Path $filePath

    if ($secret.GetType().IsArray)
    {
        return @(,$secret)
    }

    return $secret
}

function Set-Secret
{
    param (
        [string] $Name,
        [object] $Secret,
        [string] $VaultName,
        [hashtable] $AdditionalParameters
    )

    $filePath = Join-Path -Path (Get-Path) -ChildPath "${Name}.xml"
    if (Test-Path -Path $filePath)
    {
        Write-Error "Secret name, $Name, is already used in this vault."
        return $false
    }

    $Secret | Export-Clixml -Path $filePath
    return $true
}

function Remove-Secret
{
    param (
        [string] $Name,
        [string] $VaultName,
        [hashtable] $AdditionalParameters
    )

    $filePath = Join-Path -Path (Get-Path) -ChildPath "${Name}.xml"
    if (! (Test-Path -Path $filePath))
    {
        Write-Error "The secret, $Name, does not exist."
        return $false
    }

    Remove-Item -Path $filePath
    return $true
}

function Get-SecretInfo
{
    param(
        [string] $Filter,
        [string] $VaultName,
        [hashtable] $AdditionalParameters
    )

    if ([string]::IsNullOrEmpty($Filter)) { $Filter = "*" }

    $files = Get-ChildItem -Path (Join-Path -Path (Get-Path) -ChildPath "${Filter}.xml") 2>$null

    foreach ($file in $files)
    {
        $secretName = [System.IO.Path]::GetFileNameWithoutExtension((Split-Path -Path $file -Leaf))
        $secret = Import-Clixml -Path $file.FullName
        $type = if ($secret.gettype().IsArray) { [Microsoft.PowerShell.SecretsManagement.SecretType]::ByteArray }
                    elseif ($secret -is [string]) { [Microsoft.PowerShell.SecretsManagement.SecretType]::String }
                    elseif ($secret -is [securestring]) { [Microsoft.PowerShell.SecretsManagement.SecretType]::SecureString }
                    elseif ($secret -is [PSCredential]) { [Microsoft.PowerShell.SecretsManagement.SecretType]::PSCredential }
                    elseif ($secret -is [hashtable]) { [Microsoft.PowerShell.SecretsManagement.SecretType]::Hashtable }
                    else { [Microsoft.PowerShell.SecretsManagement.SecretType]::Unknown }
        
        Write-Output (
            [Microsoft.PowerShell.SecretsManagement.SecretInformation]::new(
                $secretName,
                $type,
                $VaultName)
        )
    }
}

function Test-SecretVault
{
    param (
        [string] $VaultName,
        [hashtable] $AdditionalParameters
    )

    return $true
}
