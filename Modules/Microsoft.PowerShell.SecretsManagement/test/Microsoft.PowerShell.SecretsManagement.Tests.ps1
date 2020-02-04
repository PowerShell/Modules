# Copyright (c) Microsoft Corporation. All rights reserved.
# Licensed under the MIT License.

Describe "Test Microsoft.PowerShell.SecretsManagement module" -tags CI {

    BeforeAll {

        if ((Get-Module -Name Microsoft.PowerShell.SecretsManagement -ErrorAction Ignore) -eq $null)
        {
            Import-Module -Name Microsoft.PowerShell.SecretsManagement
        }

        # Binary extension module
        $classImplementation = @'
            using Microsoft.PowerShell.SecretsManagement;
            using System;
            using System.Collections;
            using System.Collections.Generic;
            using System.Management.Automation;
            
            namespace VaultExtension
            {
                public static class Store
                {
                    private static Dictionary<string, object> _store = new Dictionary<string, object>();
                    public static Dictionary<string, object> Dict { get { return _store; } }
                }

                public class TestVault : SecretsManagementExtension
                {
                    private Dictionary<string, object> _store = Store.Dict;
            
                    public TestVault(string vaultName) : base(vaultName) { }
            
                    public override bool SetSecret(
                        string name,
                        object secret,
                        IReadOnlyDictionary<string, object> parameters,
                        out Exception error)
                    {
                        error = null;
                        if (!_store.TryAdd(name, secret))
                        {
                            error = new InvalidOperationException("SecretAlreadyExists");
                            return false;
                        }
            
                        return true;
                    }
            
                    public override object GetSecret(
                        string name,
                        IReadOnlyDictionary<string, object> parameters,
                        out Exception error)
                    {
                        error = null;
                        if (_store.TryGetValue(name, out object secret))
                        {
                            return secret;
                        }
            
                        error = new InvalidOperationException("SecretNotFoundInStore");
                        return null;
                    }
            
                    public override bool RemoveSecret(
                        string name,
                        IReadOnlyDictionary<string, object> parameters,
                        out Exception error)
                    {
                        error = null;
                        if (_store.Remove(name))
                        {
                            return true;
                        }
            
                        error = new InvalidOperationException("SecretNotRemoved");
                        return false;
                    }
            
                    public override KeyValuePair<string, string>[] GetSecretInfo(
                        string filter,
                        IReadOnlyDictionary<string, object> parameters,
                        out Exception error)
                    {
                        error = null;
                        List<KeyValuePair<string, string>> list = new List<KeyValuePair<string, string>>(_store.Count);
                        foreach (var item in _store)
                        {
                            string typeName;
                            switch (item.Value)
                            {
                                case byte[] blob:
                                    typeName = "ByteArray";
                                    break;

                                case string str:
                                    typeName = "String";
                                    break;

                                case System.Security.SecureString sstr:
                                    typeName = "SecureString";
                                    break;
                                
                                case PSCredential cred:
                                    typeName = "PSCredential";
                                    break;

                                case Hashtable ht:
                                    typeName = "Hashtable";
                                    break;

                                default:
                                    typeName = string.Empty;
                                    break;
                            }

                            list.Add(new KeyValuePair<string, string>(item.Key, typeName));
                        }
            
                        return list.ToArray();
                    }
                }
            }
'@

        $binModuleName = "TVaultBin"
        $binModulePath = Join-Path $PSScriptRoot $binModuleName
        $script:binModuleFilePath = Join-Path $binModulePath "${binModuleName}.psd1"
        $binModuleAssemblyPath = Join-Path $binModulePath "${binModuleName}.dll"
        if (! (Test-Path -Path $binModulePath))
        {
            New-Item -ItemType Directory $binModulePath -Force
            $types = Add-Type -TypeDefinition $classImplementation `
                -ReferencedAssemblies @('netstandard','Microsoft.PowerShell.SecretsManagement','System.Collections','System.Management.Automation','System.Runtime.Extensions') `
                -OutputAssembly $binModuleAssemblyPath -ErrorAction SilentlyContinue -PassThru
            
            # We have to rename the assembly file to be the same as the randomly generated assemblyl name, otherwise
            # PowerShell won't load it during module import.
            $assemblyFileName = $types[0].Module.Assembly.ManifestModule.ScopeName
            $newBinModuleAssemblyPath = Join-Path $binModulePath "${assemblyFileName}"
            Copy-Item -Path $binModuleAssemblyPath -Dest $newBinModuleAssemblyPath
            "@{ ModuleVersion = '1.0'; RequiredAssemblies = @('$assemblyFileName') }" | Out-File -FilePath $script:binModuleFilePath
        }

        # Script extension module
        $scriptImplementation = @'
            $script:store = [VaultExtension.Store]::Dict

            function Get-Secret
            {
                param (
                    [string] $Name,
                    [hashtable] $AdditionalParameters
                )

                $secret = $script:store[$Name]
                if ($secret -eq $null)
                {
                    Write-Error("CannotFindSecret")
                }

                if ($secret -is [byte[]])
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
                    [hashtable] $AdditionalParameters
                )

                return $script:store.TryAdd($Name, $Secret)
            }

            function Remove-Secret
            {
                param (
                    [string] $Name,
                    [hashtable] $AdditionalParameters
                )

                return $script:store.Remove($Name)
            }

            function Get-SecretInfo
            {
                param (
                    [string] $Filter,
                    [hashtable] $AdditionalParameters
                )

                if ([string]::IsNullOrEmpty($Filter))
                {
                    $Filter = '*'
                }
                $pattern = [WildcardPattern]::new($Filter)
                foreach ($key in $script:store.Keys)
                {
                    if ($pattern.IsMatch($key))
                    {
                        $secret = $script:store[$key]
                        $typeName = if ($secret -is [byte[]]) { "ByteArray" }
                        elseif ($secret -is [string]) { "String" }
                        elseif ($secret -is [securestring]) { "SecureString" }
                        elseif ($secret -is [PSCredential]) { "PSCredential" }
                        elseif ($secret -is [hashtable]) { "Hashtable" }
                        else { "Unknown" }

                        Write-Output ([pscustomobject] @{
                            Name = $key
                            Value = $typeName
                        })
                    }
                }
            }
'@
        $scriptModuleName = "TVaultScript"
        $scriptModulePath = Join-Path $testdrive $scriptModuleName
        New-Item -ItemType Directory $scriptModulePath -Force
        $script:scriptModuleFilePath = Join-Path $scriptModulePath "${scriptModuleName}.psd1"
        "@{ ModuleVersion = '1.0' }" | Out-File -FilePath $script:scriptModuleFilePath

        $implementingModuleName = "ImplementingModule"
        $implementingModulePath = Join-Path $scriptModulePath $implementingModuleName
        New-Item -ItemType Directory $implementingModulePath -Force
        $implementingManifestFilePath = Join-Path $implementingModulePath "${implementingModuleName}.psd1"
        $manifestInfo = "
        @{{
            ModuleVersion = '1.0'
            RootModule = '{0}'
            FunctionsToExport = @('Set-Secret','Get-Secret','Remove-Secret','Get-SecretInfo')
        }}
        " -f $implementingModuleName
        $manifestInfo | Out-File -FilePath $implementingManifestFilePath
        $implementingModuleFilePath = Join-Path $implementingModulePath "${implementingModuleName}.psm1"
        $scriptImplementation | Out-File -FilePath $implementingModuleFilePath
    }

    AfterAll {

        Unregister-SecretsVault -Name BinaryTestVault -ErrorAction Ignore
        Unregister-SecretsVault -Name ScriptTestVault -ErrorAction Ignore
    }

    Context "Built-in local store errors" {

        It "Should throw error when registering the reserved 'BuiltInLocalVault' vault name" {
            { Register-SecretsVault -Name BuiltInLocalVault -Module 'c:\' } | Should -Throw -ErrorId 'RegisterSecretsVaultInvalidVaultName'
        }
    }

    Context "Built-in local store Byte[] type" {

        $bytesToWrite = [System.Text.Encoding]::UTF8.GetBytes("Hello!!!")

        It "Verifies byte[] write to local store" {
            Add-Secret -Name __Test_ByteArray_ -Secret $bytesToWrite -Vault BuiltInLocalVault -ErrorVariable err
            $err.Count | Should -Be 0
        }

        It "Verifes byte[] read from local store" {
            $bytesRead = Get-Secret -Name __Test_ByteArray_ -Vault BuiltInLocalVault -ErrorVariable err
            $err.Count | Should -Be 0
            [System.Text.Encoding]::UTF8.GetString($bytesRead) | Should -BeExactly "Hello!!!"
        }

        It "Verifes byte[] clobber error in local store" {
            { Add-Secret -Name __Test_ByteArray_ -Secret $bytesToWrite -Vault BuiltInLocalVault -NoClobber } | Should -Throw -ErrorId "AddSecretAlreadyExists"
        }

        It "Verifies byte[] enumeration from local store" {
            $blobInfo = Get-SecretInfo -Name __Test_ByteArray_ -Vault BuiltInLocalVault -ErrorVariable err
            $err.Count | Should -Be 0
            $blobInfo.Name | Should -BeExactly "__Test_ByteArray_"
            $blobInfo.TypeName | Should -BeExactly "ByteArray"
            $blobInfo.Vault | Should -BeExactly "BuiltInLocalVault"
        }

        It "Verifies Remove byte[] secret" {
            { Remove-Secret -Name __Test_ByteArray_ -Vault BuiltInLocalVault -ErrorVariable err } | Should -Not -Throw
            $err.Count | Should -Be 0
            { Get-Secret -Name __Test_ByteArray_ -Vault BuiltInLocalVault -ErrorAction Stop } | Should -Throw -ErrorId 'GetSecretNotFound,Microsoft.PowerShell.SecretsManagement.GetSecretCommand'
        }
    }

    Context "Built-in local store String type" {

        It "Verifes string write to local store" {
            Add-Secret -Name __Test_String_ -Secret "Hello!!Secret" -Vault BuiltInLocalVault -ErrorVariable err
            $err.Count | Should -Be 0
        }

        It "Verifies string read from local store" {
            $strRead = Get-Secret -Name __Test_String_ -Vault BuiltInLocalVault -ErrorVariable err
            $err.Count | Should -Be 0
            ($strRead -is [SecureString]) | Should -BeTrue

            $strRead = Get-Secret -Name __Test_String_ -Vault BuiltInLocalVault -AsPlainText -ErrorVariable err
            $err.Count | Should -Be 0
            $strRead | Should -BeExactly "Hello!!Secret"
        }

        It "Verifies string enumeration from local store" {
            $strInfo = Get-SecretInfo -Name __Test_String_ -Vault BuiltInLocalVault -ErrorVariable err
            $err.Count | Should -Be 0
            $strInfo.Name | Should -BeExactly "__Test_String_"
            $strInfo.TypeName | Should -BeExactly "String"
            $strInfo.Vault | Should -BeExactly "BuiltInLocalVault"
        }

        It "Verifies string remove from local store" {
            { Remove-Secret -Name __Test_String_ -Vault BuiltInLocalVault -ErrorVariable err } | Should -Not -Throw
            $err.Count | Should -Be 0
            { Get-Secret -Name __Test_String_ -Vault BuiltInLocalVault -ErrorAction Stop } | Should -Throw -ErrorId 'GetSecretNotFound,Microsoft.PowerShell.SecretsManagement.GetSecretCommand'
        }
    }

    Context "Built-in local store SecureString type" {

	$randomSecret = [System.IO.Path]::GetRandomFileName()
        $secureStringToWrite = ConvertTo-SecureString -String $randomSecret -AsPlainText -Force

        It "Verifies SecureString write to local store" {
            Add-Secret -Name __Test_SecureString_ -Secret $secureStringToWrite -Vault BuiltInLocalVault -ErrorVariable err
            $err.Count | Should -Be 0
        }

        It "Verifies SecureString read from local store" {
            $ssRead = Get-Secret -Name __Test_SecureString_ -Vault BuiltInLocalVault -ErrorVariable err
            $err.Count | Should -Be 0
            [System.Net.NetworkCredential]::new('',$ssRead).Password | Should -BeExactly $randomSecret
        }

        It "Verifies SecureString enumeration from local store" {
            $ssInfo = Get-SecretInfo -Name __Test_SecureString_ -Vault BuiltInLocalVault -ErrorVariable err
            $err.Count | Should -Be 0
            $ssInfo.Name | Should -BeExactly "__Test_SecureString_"
            $ssInfo.TypeName | Should -BeExactly "SecureString"
            $ssInfo.Vault | Should -BeExactly "BuiltInLocalVault"
        }

        It "Verifies SecureString remove from local store" {
            { Remove-Secret -Name __Test_SecureString_ -Vault BuiltInLocalVault -ErrorVariable err } | Should -Not -Throw
            $err.Count | Should -Be 0
            { Get-Secret -Name __Test_SecureString_ -Vault BuiltInLocalVault -ErrorAction Stop } | Should -Throw `
                -ErrorId 'GetSecretNotFound,Microsoft.PowerShell.SecretsManagement.GetSecretCommand'
        }
    }

    Context "Built-in local store PSCredential type" {

        $randomSecret = [System.IO.Path]::GetRandomFileName()

        It "Verifies PSCredential type write to local store" {
            $cred = [pscredential]::new('UserL', (ConvertTo-SecureString $randomSecret -AsPlainText -Force))
            Add-Secret -Name __Test_PSCredential_ -Secret $cred -Vault BuiltInLocalVault -ErrorVariable err
            $err.Count | Should -Be 0
        }

        It "Verifies PSCredential read from local store" {
            $cred = Get-Secret -Name __Test_PSCredential_ -Vault BuiltInLocalVault -ErrorVariable err
            $err.Count | Should -Be 0
            $cred.UserName | Should -BeExactly "UserL"
            [System.Net.NetworkCredential]::new('', ($cred.Password)).Password | Should -BeExactly $randomSecret
        }

        It "Verifies PSCredential enumeration from local store" {
            $credInfo = Get-SecretInfo -Name __Test_PSCredential_ -Vault BuiltInLocalVault -ErrorVariable err
            $credInfo.Name | Should -BeExactly "__Test_PSCredential_"
            $credInfo.TypeName | Should -BeExactly "PSCredential"
            $credInfo.Vault | Should -BeExactly "BuiltInLocalVault"
        }

        It "Verifies PSCredential remove from local store" {
            Remove-Secret -Name __Test_PSCredential_ -Vault BuiltInLocalVault -ErrorVariable err
            $err.Count | Should -Be 0
            { Get-Secret -Name __Test_PSCredential_ -Vault BuiltInLocalVault -ErrorAction Stop } | Should -Throw `
                -ErrorId 'GetSecretNotFound,Microsoft.PowerShell.SecretsManagement.GetSecretCommand'
        }
    }

    Context "Built-in local store Hashtable type" {
        $randomSecretA = [System.IO.Path]::GetRandomFileName()
        $randomSecretB = [System.IO.Path]::GetRandomFileName()

        It "Verifies Hashtable type write to local store" {
            $ht = @{
                Blob = ([byte[]] @(1,2))
                Str = "Hello"
                SecureString = (ConvertTo-SecureString $randomSecretA -AsPlainText -Force)
                Cred = ([pscredential]::New("UserA", (ConvertTo-SecureString $randomSecretB -AsPlainText -Force)))
            }
            Add-Secret -Name __Test_Hashtable_ -Secret $ht -Vault BuiltInLocalVault -ErrorVariable err
            $err.Count | Should -Be 0
        }

        It "Verifies Hashtable read from local store" {
            $ht = Get-Secret -Name __Test_Hashtable_ -Vault BuiltInLocalVault -ErrorVariable err
            $err.Count | Should -Be 0
            $ht.Blob.Count | Should -Be 2
            $ht.Str | Should -BeExactly "Hello"
            [System.Net.NetworkCredential]::new('', ($ht.SecureString)).Password | Should -BeExactly $randomSecretA
            $ht.Cred.UserName | Should -BeExactly "UserA"
            [System.Net.NetworkCredential]::New('', ($ht.Cred.Password)).Password | Should -BeExactly $randomSecretB
        }

        It "Verifies Hashtable enumeration from local store" {
            $htInfo = Get-SecretInfo -Name __Test_Hashtable_ -Vault BuiltInLocalVault -ErrorVariable err
            $err.Count | Should -Be 0
            $htInfo.Name | Should -BeExactly "__Test_Hashtable_"
            $htInfo.TypeName | Should -BeExactly "Hashtable"
            $htInfo.Vault | Should -BeExactly "BuiltInLocalVault"
        }

        It "Verifies Hashtable remove from local store" {
            Remove-Secret -Name __Test_Hashtable_ -Vault BuiltInLocalVault -ErrorVariable err
            $err.Count | Should -Be 0
            { Get-Secret -Name __Test_Hashtable_ -Vault BuiltInLocalVault -ErrorAction Stop } | Should -Throw `
                -ErrorId 'GetSecretNotFound,Microsoft.PowerShell.SecretsManagement.GetSecretCommand'
        }
    }

    function VerifyByteArrayType
    {
        param (
            [string] $Title,
            [string] $VaultName
        )

        It "Verifies writing byte[] type to $Title vault" {
            $bytes = [System.Text.Encoding]::UTF8.GetBytes("BinVaultHelloStr")
            Add-Secret -Name BinVaultBlob -Secret $bytes -Vault $VaultName -ErrorVariable err
            $err.Count | Should -Be 0
        }

        It "Verifies reading byte[] type from $Title vault" {
            $blob = Get-Secret -Name BinVaultBlob -Vault $VaultName -ErrorVariable err
            $err.Count | Should -Be 0
            [System.Text.Encoding]::UTF8.GetString($blob) | Should -BeExactly "BinVaultHelloStr"
        }

        It "Verifies enumerating byte[] type from $Title vault" {
            $blobInfo = Get-SecretInfo -Name BinVaultBlob -Vault $VaultName -ErrorVariable err
            $err.Count | Should -Be 0
            $blobInfo.Name | Should -BeExactly "BinVaultBlob"
            $blobInfo.TypeName | Should -BeExactly "ByteArray"
            $blobInfo.Vault | Should -BeExactly $VaultName
        }

        It "Verifies removing byte[] type from $Title vault" {
            Remove-Secret -Name BinVaultBlob -Vault $VaultName -ErrorVariable err
            $err.Count | Should -Be 0
            { Get-Secret -Name BinVaultBlob -Vault $VaultName -ErrorAction Stop } | Should -Throw `
                -ErrorId 'InvokeGetSecretError,Microsoft.PowerShell.SecretsManagement.GetSecretCommand'
        }
    }

    function VerifyStringType
    {
        param (
            [string] $Title,
            [string] $VaultName
        )

        It "Verifies writing string type to $Title vault" {
            Add-Secret -Name BinVaultStr -Secret "HelloBinVault" -Vault $VaultName -ErrorVariable err
            $err.Count | Should -Be 0
        }

        It "Verifies reading string type from $Title vault" {
            $str = Get-Secret -Name BinVaultStr -Vault $VaultName -ErrorVariable err
            $err.Count | Should -Be 0
            ($str -is [SecureString]) | Should -BeTrue

            $str = Get-Secret -Name BinVaultStr -Vault $VaultName -AsPlainText -ErrorVariable err
            $err.Count | Should -Be 0
            $str | Should -BeExactly "HelloBinVault"
        }

        It "Verifies enumerating string type from $Title vault" {
            $strInfo = Get-SecretInfo -Name BinVaultStr -Vault $VaultName -ErrorVariable err
            $err.Count | Should -Be 0
            $strInfo.Name | Should -BeExactly "BinVaultStr"
            $strInfo.TypeName | Should -BeExactly "String"
            $strInfo.Vault | Should -BeExactly $VaultName
        }

        It "Verifies removing string type from $Title vault" {
            Remove-Secret -Name BinVaultStr -Vault $VaultName -ErrorVariable err
            $err.Count | Should -Be 0
            { Get-Secret -Name BinVaultStr -Vault $VaultName -ErrorAction Stop } | Should -Throw `
                -ErrorId 'InvokeGetSecretError,Microsoft.PowerShell.SecretsManagement.GetSecretCommand'
        }
    }

    function VerifySecureStringType
    {
        param (
            [string] $Title,
            [string] $VaultName
        )

        $randomSecret = [System.IO.Path]::GetRandomFileName()

        It "Verifies writing SecureString type to $Title vault" {
            Add-Secret -Name BinVaultSecureStr -Secret (ConvertTo-SecureString $randomSecret -AsPlainText -Force) `
                -Vault $VaultName -ErrorVariable err
            $err.Count | Should -Be 0
        }

        It "Verifies reading SecureString type from $Title vault" {
            $ss = Get-Secret -Name BinVaultSecureStr -Vault $VaultName -ErrorVariable err
            $err.Count | Should -Be 0
            [System.Net.NetworkCredential]::new('',$ss).Password | Should -BeExactly $randomSecret
        }

        It "Verifies enumerating SecureString type from $Title vault" {
            $ssInfo = Get-SecretInfo -Name BinVaultSecureStr -Vault $VaultName -ErrorVariable err
            $err.Count | Should -Be 0
            $ssInfo.Name | Should -BeExactly "BinVaultSecureStr"
            $ssInfo.TypeName | Should -BeExactly "SecureString"
            $ssInfo.Vault | Should -BeExactly $VaultName
        }

        It "Verifies removing SecureString type from $Title vault" {
            Remove-Secret -Name BinVaultSecureStr -Vault $VaultName -ErrorVariable err
            $err.Count | Should -Be 0
            { Get-Secret -Name BinVaultSecureStr -Vault $VaultName -ErrorAction Stop } | Should -Throw `
                -ErrorId 'InvokeGetSecretError,Microsoft.PowerShell.SecretsManagement.GetSecretCommand'
        }
    }

    function VerifyPSCredentialType
    {
        param (
            [string] $Title,
            [string] $VaultName
        )

        $randomSecret = [System.IO.Path]::GetRandomFileName()

        It "Verifies writing PSCredential to $Title vault" {
            $cred = [pscredential]::new('UserName', (ConvertTo-SecureString $randomSecret -AsPlainText -Force))
            Add-Secret -Name BinVaultCred -Secret $cred -Vault $VaultName -ErrorVariable err
            $err.Count | Should -Be 0
        }

        It "Verifies reading PSCredential type from $Title vault" {
            $cred = Get-Secret -Name BinVaultCred -Vault $VaultName -ErrorVariable err
            $err.Count | Should -Be 0
            $cred.UserName | Should -BeExactly "UserName"
            [System.Net.NetworkCredential]::new('', ($cred.Password)).Password | Should -BeExactly $randomSecret
        }

        It "Verifies enumerating PSCredential type from $Title vault" {
            $credInfo = Get-SecretInfo -Name BinVaultCred -Vault $VaultName -ErrorVariable err
            $err.Count | Should -Be 0
            $credInfo.Name | Should -BeExactly "BinVaultCred"
            $credInfo.TypeName | Should -BeExactly "PSCredential"
            $credInfo.Vault | Should -BeExactly $VaultName
        }

        It "Verifies removing PSCredential type from $Title vault" {
            Remove-Secret -Name BinVaultCred -Vault $VaultName -ErrorVariable err
            $err.Count | Should -Be 0
            { Get-Secret -Name BinVaultCred -Vault $VaultName -ErrorAction Stop } | Should -Throw `
                -ErrorId 'InvokeGetSecretError,Microsoft.PowerShell.SecretsManagement.GetSecretCommand'
        }
    }

    function VerifyHashType
    {
        param (
            [string] $Title,
            [string] $VaultName
        )

        $randomSecretA = [System.IO.Path]::GetRandomFileName()
        $randomSecretB = [System.IO.Path]::GetRandomFileName()

        It "Verifies writing Hashtable type to $Title vault" {
            $ht = @{ 
                Blob = ([byte[]] @(1,2))
                Str = "Hello"
                SecureString = (ConvertTo-SecureString $randomSecretA -AsPlainText -Force)
                Cred = ([pscredential]::New("UserA", (ConvertTo-SecureString $randomSecretB -AsPlainText -Force)))
            }
            Add-Secret -Name BinVaultHT -Vault $VaultName -Secret $ht -ErrorVariable err
            $err.Count | Should -Be 0
        }

        It "Verifies reading Hashtable type from $Title vault" {
            $ht = Get-Secret -Name BinVaultHT -Vault $VaultName -ErrorVariable err
            $err.Count | Should -Be 0
            $ht.Blob.Count | Should -Be 2
            $ht.Str | Should -BeExactly "Hello"
            [System.Net.NetworkCredential]::new('', $ht.SecureString).Password | Should -BeExactly $randomSecretA
            $ht.Cred.UserName | Should -BeExactly "UserA"
            [System.Net.NetworkCredential]::new('', $ht.Cred.Password).Password | Should -BeExactly $randomSecretB
        }

        It "Verifies enumerating Hashtable type from $Title vault" {
            $htInfo = Get-SecretInfo -Name BinVaultHT -Vault $VaultName -ErrorVariable err
            $err.Count | Should -Be 0
            $htInfo.Name | Should -BeExactly "BinVaultHT"
            $htInfo.TypeName | Should -BeExactly "Hashtable"
            $htInfo.Vault | Should -BeExactly $VaultName
        }

        It "Verifies removing Hashtable type from $Title vault" {
            Remove-Secret -Name BinVaultHT -Vault $VaultName -ErrorVariable err
            $err.Count | Should -Be 0
            { Get-Secret -Name BinVaultHT -Vault $VaultName -ErrorAction Stop } | Should -Throw `
                -ErrorId 'InvokeGetSecretError,Microsoft.PowerShell.SecretsManagement.GetSecretCommand'
        }
    }

    Context "Binary extension vault registration tests" {

        It "Should register the binary test vault extension successfully" {
            { Register-SecretsVault -Name BinaryTestVault -ModuleName $script:binModuleFilePath -VaultParameters @{ Param1 = "Hello" } -ErrorVariable err } | Should -Not -Throw
            $err.Count | Should -Be 0
        }

        It "Should throw error when registering existing registered vault extension" {
            { Register-SecretsVault -Name BinaryTestVault -ModuleName $script:binModuleFilePath -VaultParameters @{ Param1 = "Hello" } } | Should -Throw -ErrorId 'RegisterSecretsVaultInvalidVaultName'
        }

    }

    Context "Binary extension vault byte[] type tests" {

        [VaultExtension.Store]::Dict.Clear()

        VerifyByteArrayType -Title "binary" -VaultName "BinaryTestVault"
    }

    Context "Binary extension vault string type tests" {

        [VaultExtension.Store]::Dict.Clear()

        VerifyStringType -Title "binary" -VaultName "BinaryTestVault"
    }

    Context "Binary extension vault SecureString type tests" {

        [VaultExtension.Store]::Dict.Clear()

        VerifySecureStringType -Title "binary" -VaultName "BinaryTestVault"
    }

    Context "Binary extension vault PSCredential type tests" {

        [VaultExtension.Store]::Dict.Clear()

        VerifyPSCredentialType -Title "binary" -VaultName "BinaryTestVault"
    }

    Context "Binary extension vault Hashtable type tests" {

        [VaultExtension.Store]::Dict.Clear()

        VerifyHashType -Title "binary" -VaultName "BinaryTestVault"
    }

    Context "Script extension vault tests" {

        It "Should register the script test vault extension successfully" {
            { Register-SecretsVault -Name ScriptTestVault -ModuleName $script:scriptModuleFilePath -VaultParameters @{ Param1 = "Hello" } -ErrorVariable err } | Should -Not -Throw
            $err.Count | Should -Be 0
        }

        It "Should throw error when registering existing registered vault extension" {
            { Register-SecretsVault -Name ScriptTestVault -ModuleName $script:binModuleFilePath -VaultParameters @{ Param1 = "Hello" } } | Should -Throw -ErrorId 'RegisterSecretsVaultInvalidVaultName'
        }
    }

    Context "Script extension vault byte[] type tests" {

        [VaultExtension.Store]::Dict.Clear()

        VerifyByteArrayType -Title "script" -VaultName "ScriptTestVault"
    }

    Context "Script extension vault String type tests" {

        [VaultExtension.Store]::Dict.Clear()

        VerifyStringType -Title "script" -VaultName "ScriptTestVault"
    }

    Context "Script extension vault SecureString type tests" {

        [VaultExtension.Store]::Dict.Clear()

        VerifySecureStringType -Title "script" -VaultName "ScriptTestVault"
    }

    Context "Script extension vault PSCredential type tests" {

        [VaultExtension.Store]::Dict.Clear()

        VerifyPSCredentialType -Title "script" -VaultName "ScriptTestVault"
    }

    Context "Script extension vault Hashtable type tests" {

        [VaultExtension.Store]::Dict.Clear()

        VerifyHashType -Title "script" -VaultName "ScriptTestVault"
    }
}
