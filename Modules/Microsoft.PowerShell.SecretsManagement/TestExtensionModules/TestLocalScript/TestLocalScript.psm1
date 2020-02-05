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

    return Import-Clixml -Path $filePath
}

function Set-Secret
{
    param (
        [string] $Name,
        [object] $Secret,
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
        [hashtable] $AdditionalParameters
    )

    if ([string]::IsNullOrEmpty($Filter)) { $Filter = "*" }

    $files = Get-ChildItem -Path (Join-Path -Path (Get-Path) -ChildPath "${Filter}.xml") 2>$null

    foreach ($file in $files)
    {
        $secretName = [System.IO.Path]::GetFileNameWithoutExtension((Split-Path -Path $file -Leaf))
        $secret = Import-Clixml -Path $file.FullName
        $typeName = if ($secret -is [byte[]]) { "ByteArray" }
                    elseif ($secret -is [string]) { "String" }
                    elseif ($secret -is [securestring]) { "SecureString" }
                    elseif ($secret -is [PSCredential]) { "PSCredential" }
                    elseif ($secret -is [hashtable]) { "Hashtable" }
                    else { "Unknown" }

        Write-Output ([pscustomobject] @{
            Name = $secretName
            Value = $typeName
        })
    }
}
