# Copyright (c) Microsoft Corporation. All rights reserved.
# Licensed under the MIT License.

Describe "Test Microsoft.PowerShell.SecretsManagement module" -tags CI {

    BeforeAll {

        Import-Module -Name Microsoft.PowerShell.SecretsManagement;

        # Binary
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
            $type = Add-Type -TypeDefinition $classImplementation `
                -ReferencedAssemblies @('netstandard','Microsoft.PowerShell.SecretsManagement','System.Collections','System.Management.Automation','System.Runtime.Extensions') `
                -OutputAssembly $binModuleAssemblyPath -ErrorAction SilentlyContinue -PassThru
            
            # We have to rename the assembly file to be the same as the randomly generated assemblyl name, otherwise
            # PowerShell won't load it during module import.
            $assemblyFileName = $type.Module.Assembly.ManifestModule.ScopeName
            if ($assemblyFileName.Count -gt 1) { $assemblyFileName = $assemblyFileName[0] }
            $newBinModuleAssemblyPath = Join-Path $binModulePath "${assemblyFileName}"
            Copy-Item -Path $binModuleAssemblyPath -Dest $newBinModuleAssemblyPath
            "@{ ModuleVersion = '1.0'; RequiredAssemblies = @('$assemblyFileName') }" | Out-File -FilePath $script:binModuleFilePath
        }

        # Script
        $scriptImplementation = @'
            $script:store = @{}

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

                return $secret
            }

            function Set-Secret
            {
                param (
                    [string] $Name,
                    [object] $Secret,
                    [hashtable] $AdditionalParameters
                )

                $script:store.Add($Name, $Secret)
                return $?
            }

            function Remove-Secret
            {
                param (
                    [string] $Name,
                    [hashtable] $AdditionalParameters
                )

                $script:store.Remove($Name)
                return $?
            }

            function Get-SecretInfo
            {
                param (
                    [string] $Filter,
                    [hashtable] $AdditionalParameters
                )

                foreach ($key in $global:store.Keys)
                {
                    Write-Output ([pscustomobject] @{
                        Name = $key
                        Value = ($global:store[$key]).GetType().FullName
                    })
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
            $strRead | Should -BeExactly "Hello!!Secret"
        }

        It "Verifies string remove from local store" {
            { Remove-Secret -Name __Test_String_ -Vault BuiltInLocalVault -ErrorVariable err } | Should -Not -Throw
            $err.Count | Should -Be 0
            { Get-Secret -Name __Test_String_ -Vault BuiltInLocalVault -ErrorAction Stop } | Should -Throw -ErrorId 'GetSecretNotFound,Microsoft.PowerShell.SecretsManagement.GetSecretCommand'
        }
    }

    Context "Built-in local store SecureString type" {

        $secureStringToWrite = ConvertTo-SecureString -String "SSHello!!!" -AsPlainText -Force

        It "Verifies SecureString write to local store" {
            Add-Secret -Name __Test_SecureString_ -Secret $secureStringToWrite -Vault BuiltInLocalVault -ErrorVariable err
            $err.Count | Should -Be 0
        }

        It "Verifies SecureString read from local store" {
            $ssRead = Get-Secret -Name __Test_SecureString_ -Vault BuiltInLocalVault -ErrorVariable err
            $err.Count | Should -Be 0
            [System.Net.NetworkCredential]::new('',$ssRead).Password | Should -BeExactly 'SSHello!!!'
        }

        It "Verifies SecureString remove from local store" {
            { Remove-Secret -Name __Test_SecureString_ -Vault BuiltInLocalVault -ErrorVariable err } | Should -Not -Throw
            $err.Count | Should -Be 0
            { Get-Secret -Name __Test_SecureString_ -Vault BuiltInLocalVault -ErrorAction Stop } | Should -Throw -ErrorId 'GetSecretNotFound,Microsoft.PowerShell.SecretsManagement.GetSecretCommand'
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

        It "Verifies writing byte[] type to binary vault" {
            $bytes = [System.Text.Encoding]::UTF8.GetBytes("BinVaultHelloStr")
            Add-Secret -Name BinVaultBlob -Secret $bytes -Vault BinaryTestVault -ErrorVariable err
            $err.Count | Should -Be 0
        }

        It "Verifies reading byte[] type from binary vault" {
            $blob = Get-Secret -Name BinVaultBlob -Vault BinaryTestVault -ErrorVariable err
            $err.Count | Should -Be 0
            [System.Text.Encoding]::UTF8.GetString($blob) | Should -BeExactly "BinVaultHelloStr"
        }

        It "Verifies enumerating byte[] type from binary vault" {
            $blobInfo = Get-SecretInfo -Name BinVaultBlob -Vault BinaryTestVault -ErrorVariable err
            $err.Count | Should -Be 0
            $blobInfo.Name | Should -BeExactly "BinVaultBlob"
            $blobInfo.TypeName | Should -BeExactly "ByteArray"
            $blobInfo.Vault | Should -BeExactly "BinaryTestVault"
        }

        It "Verifies removing byte[] type from binary vault" {
            Remove-Secret -Name BinVaultBlob -Vault BinaryTestVault -ErrorVariable err
            $err.Count | Should -Be 0
            { Get-Secret -Name BinVaultBlob -Vault BinaryTestVault -ErrorAction Stop } | Should -Throw `
                -ErrorId 'InvokeGetSecretError,Microsoft.PowerShell.SecretsManagement.GetSecretCommand'
        }
    }

    Context "Binary extension vault string type tests" {

        [VaultExtension.Store]::Dict.Clear()

        It "Verifies writing string type to binary vault" {
            Add-Secret -Name BinVaultStr -Secret "HelloBinVault" -Vault BinaryTestVault -ErrorVariable err
            $err.Count | Should -Be 0
        }

        It "Verifies reading string type from binary vault" {
            $str = Get-Secret -Name BinVaultStr -Vault BinaryTestVault -ErrorVariable err
            $err.Count | Should -Be 0
            $str | Should -BeExactly "HelloBinVault"
        }

        It "Verifies enumerating string type from binary vault" {
            $strInfo = Get-SecretInfo -Name BinVaultStr -Vault BinaryTestVault -ErrorVariable err
            $err.Count | Should -Be 0
            $strInfo.Name | Should -BeExactly "BinVaultStr"
            $strInfo.TypeName | Should -BeExactly "String"
            $strInfo.Vault | Should -BeExactly "BinaryTestVault"
        }

        It "Verifies removing string type from binary vault" {
            Remove-Secret -Name BinVaultStr -Vault BinaryTestVault -ErrorVariable err
            $err.Count | Should -Be 0
            { Get-Secret -Name BinVaultStr -Vault BinaryTestVault -ErrorAction Stop } | Should -Throw `
                -ErrorId 'InvokeGetSecretError,Microsoft.PowerShell.SecretsManagement.GetSecretCommand'
        }
    }

    Context "Binary extension vault SecureString type tests" {

        [VaultExtension.Store]::Dict.Clear()

        It "Verifies writing SecureString type to binary vault" {
            Add-Secret -Name BinVaultSecureStr -Secret (ConvertTo-SecureString "BinVaultSecureStr" -AsPlainText -Force) `
                -Vault BinaryTestVault -ErrorVariable err
            $err.Count | Should -Be 0
        }

        It "Verifies reading SecureString type from binary vault" {
            $ss = Get-Secret -Name BinVaultSecureStr -Vault BinaryTestVault -ErrorVariable err
            $err.Count | Should -Be 0
            [System.Net.NetworkCredential]::new('',$ss).Password | Should -BeExactly "BinVaultSecureStr"
        }

        It "Verifies enumerating SecureString type from binary vault" {
            $ssInfo = Get-SecretInfo -Name BinVaultSecureStr -Vault BinaryTestVault -ErrorVariable err
            $err.Count | Should -Be 0
            $ssInfo.Name | Should -BeExactly "BinVaultSecureStr"
            $ssInfo.TypeName | Should -BeExactly "SecureString"
            $ssInfo.Vault | Should -BeExactly "BinaryTestVault"
        }

        It "Verifies removing SecureString type from binary vault" {
            Remove-Secret -Name BinVaultSecureStr -Vault BinaryTestVault -ErrorVariable err
            $err.Count | Should -Be 0
            { Get-Secret -Name BinVaultSecureStr -Vault BinaryTestVault -ErrorAction Stop } | Should -Throw `
                -ErrorId 'InvokeGetSecretError,Microsoft.PowerShell.SecretsManagement.GetSecretCommand'
        }
    }

    Context "Binary extension vault PSCredential type tests" {

        [VaultExtension.Store]::Dict.Clear()

        It "Verifies writing PSCredential to binary vault" {
            $cred = [pscredential]::new('UserName', (ConvertTo-SecureString "UserSecret" -AsPlainText -Force))
            Add-Secret -Name BinVaultCred -Secret $cred -Vault BinaryTestVault -ErrorVariable err
            $err.Count | Should -Be 0
        }

        It "Verifies reading PSCredential type from binary vault" {
            $cred = Get-Secret -Name BinVaultCred -Vault BinaryTestVault -ErrorVariable err
            $err.Count | Should -Be 0
            $cred.UserName | Should -BeExactly "UserName"
            [System.Net.NetworkCredential]::new('', ($cred.Password)).Password | Should -BeExactly "UserSecret"
        }

        It "Verifies enumerating PSCredential type from binary vault" {
            $credInfo = Get-SecretInfo -Name BinVaultCred -Vault BinaryTestVault -ErrorVariable err
            $err.Count | Should -Be 0
            $credInfo.Name | Should -BeExactly "BinVaultCred"
            $credInfo.TypeName | Should -BeExactly "PSCredential"
            $credInfo.Vault | Should -BeExactly "BinaryTestVault"
        }

        It "Verifies removing PSCredential type from binary vault" {
            Remove-Secret -Name BinVaultCred -Vault BinaryTestVault -ErrorVariable err
            $err.Count | Should -Be 0
            { Get-Secret -Name BinVaultCred -Vault BinaryTestVault -ErrorAction Stop } | Should -Throw `
                -ErrorId 'InvokeGetSecretError,Microsoft.PowerShell.SecretsManagement.GetSecretCommand'
        }
    }

    Context "Binary extension vault Hashtable type tests" {

        [VaultExtension.Store]::Dict.Clear()

        It "Verifies writing Hashtable to binary vault" {
            $ht = @{ 
                Blob = ([byte[]] @(1,2))
                Str = "Hello"
                SecureString = (ConvertTo-SecureString "SecureHello" -AsPlainText -Force)
                Cred = ([pscredential]::New("UserA", (ConvertTo-SecureString "UserASecret" -AsPlainText -Force)))
            }
            Add-Secret -Name BinVaultHT -Vault BinaryTestVault -Secret $ht -ErrorVariable err
            $err.Count | Should -Be 0
        }

        It "Verifies reading Hashtable from binary vault" {
            $ht = Get-Secret -Name BinVaultHT -Vault BinaryTestVault -ErrorVariable err
            $err.Count | Should -Be 0
            $ht.Blob.Count | Should -Be 2
            $ht.Str | Should -BeExactly "Hello"
            [System.Net.NetworkCredential]::new('', $ht.SecureString).Password | Should -BeExactly "SecureHello"
            $ht.Cred.UserName | Should -BeExactly "UserA"
            [System.Net.NetworkCredential]::new('', $ht.Cred.Password).Password | Should -BeExactly "UserASecret"
        }

        # TODO:
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
}
