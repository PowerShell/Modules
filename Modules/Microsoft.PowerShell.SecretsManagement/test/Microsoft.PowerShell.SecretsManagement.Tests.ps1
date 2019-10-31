# Copyright (c) Microsoft Corporation. All rights reserved.
# Licensed under the MIT License.

Describe "Test Microsoft.PowerShell.SecretsManagement module" -tags CI {

    BeforeAll {

        Import-Module -Name Microsoft.PowerShell.SecretsManagement;

        # Binary
        $classImplementation = @'
            using Microsoft.PowerShell.SecretsManagement;
            using System;
            using System.Collections.Generic;
            
            namespace VaultExtension
            {
                public class TestVault : SecretsManagementExtension
                {
                    private Dictionary<string, object> _store = new Dictionary<string, object>();
            
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
                            item.Value.GetType().ToString();
                            list.Add(new KeyValuePair<string, string>(item.Key, item.Value.GetType().ToString()));
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
                -ReferencedAssemblies @('netstandard','Microsoft.PowerShell.SecretsManagement','System.Collections') `
                -OutputAssembly $binModuleAssemblyPath -ErrorAction SilentlyContinue -PassThru
            
            # We have to rename the assembly file to be the same as the randomly generated assemblyl name, otherwise
            # PowerShell won't load it during module import.
            $assemblyFileName = $type.Module.Assembly.ManifestModule.ScopeName
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

            Export-ModuleMember -Function 'Get-Secret','Set-Secret','Remove-Secret','Get-SecretInfo'
'@
        $scriptModuleName = "TVaultScript"
        $scriptModulePath = Join-Path $testdrive $scriptModuleName
        New-Item -ItemType Directory $scriptModulePath -Force
        $script:scriptModuleFilePath = Join-Path $scriptModulePath "${scriptModuleName}.psm1"
        $scriptImplementation | Out-File -FilePath $script:scriptModuleFilePath
    }

    AfterAll {

        Unregister-SecretsVault -Name BinaryTestVault -ErrorAction Ignore
        Unregister-SecretsVault -Name ScriptTestVault -ErrorAction Ignore
    }

    Context "Built-in local store tests" {

        It "Should throw error when registering the reserved It 'BuiltInLocalVault' name" {
            { Register-SecretsVault -Name BuiltInLocalVault -ModulePath 'c:\' } | Should -Throw -ErrorId 'RegisterSecretsVaultInvalidVaultName'
        }

    }

    Context "Binary extension vault tests" {

        It "Should register the binary test vault extension successfully" {
            { Register-SecretsVault -Name BinaryTestVault -ModulePath $script:binModuleFilePath -VaultParameters @{ Param1 = "Hello" } -ErrorVariable err } | Should -Not -Throw
            $err | Should -Be $null
        }

        It "Should throw error when registering existing registered vault extension" {
            { Register-SecretsVault -Name BinaryTestVault -ModulePath $script:binModuleFilePath -VaultParameters @{ Param1 = "Hello" } } | Should -Throw -ErrorId 'RegisterSecretsVaultInvalidVaultName'
        }

        # Verify registered vault

        # Expected errors
    }

    Context "Script extension vault tests" {

        It "Should register the script test vault extension successfully" {
            { Register-SecretsVault -Name ScriptTestVault -ModulePath $script:scriptModuleFilePath -VaultParameters @{ Param1 = "Hello" } -ErrorVariable err } | Should -Not -Throw
            $err | Should -Be $null
        }

        It "Should throw error when registering existing registered vault extension" {
            { Register-SecretsVault -Name ScriptTestVault -ModulePath $script:binModuleFilePath -VaultParameters @{ Param1 = "Hello" } } | Should -Throw -ErrorId 'RegisterSecretsVaultInvalidVaultName'
        }
    }
}
