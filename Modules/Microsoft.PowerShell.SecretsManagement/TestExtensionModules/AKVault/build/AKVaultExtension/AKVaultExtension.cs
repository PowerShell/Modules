using Microsoft.PowerShell.SecretsManagement;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Security;

namespace AKVaultExtension
{
    #region AKVaultExtension

    public sealed class AKVaultExtension : SecretsManagementExtension
    {
        #region Scripts

        private const string CheckSubscriptionLogIn = @"
            param ([string] $SubscriptionId)

            Import-Module -Name Az.Accounts
            
            $azContext = Az.Accounts\Get-AzContext
            return (($azContext -ne $null) -and ($azContext.Subscription.Id -eq $SubscriptionId))
        ";

        private const string GetSecretScript = @"
            param (
                [string] $Name,
                [string] $VaultName
            )

            Import-Module -Name Az.KeyVault

            $secret = Az.KeyVault\Get-AzKeyVaultSecret -Name $Name -VaultName $VaultName
            if ($secret -ne $null)
            {
                Write-Output $secret.SecretValue
            }
        ";

        private const string SetSecretScript = @"
            param (
                [string] $Name,
                [SecureString] $Secret,
                [string] $VaultName
            )

            Import-Module -Name Az.KeyVault

            Az.KeyVault\Set-AzKeyVaultSecret -Name $Name -SecretValue $Secret -VaultName $VaultName
        ";

        private const string RemoveSecretScript = @"
            param (
                [string] $Name,
                [string] $VaultName
            )

            Import-Module -Name Az.KeyVault

            Az.KeyVault\Remove-AzKeyVaultSecret -Name $Name -VaultName $VaultName -Force
        ";

        private const string EnumerateSecretsScript = @"
            param (
                [string] $Filter,
                [string] $VaultName
            )

            if ([string]::IsNullOrEmpty($Filter))
            {
                $Filter = ""*""
            }

            $pattern = [WildcardPattern]::new($Filter)
            $vaultSecretInfos = Az.KeyVault\Get-AzKeyVaultSecret -VaultName $VaultName
            foreach ($vaultSecretInfo in $vaultSecretInfos)
            {
                if ($pattern.IsMatch($vaultSecretInfo.Name))
                {
                    Write-Output ([pscustomobject] @{
                        Name = $vaultSecretInfo.Name
                    })
                }
            }
        ";

        #endregion

        #region Constructors

        private AKVaultExtension() : base(string.Empty) { }

        public AKVaultExtension(string vaultName) : base(vaultName) { }

        #endregion

        #region Abstract implementations

        public override object GetSecret(
            string name, 
            IReadOnlyDictionary<string, object> parameters, 
            out Exception error)
        {
            string azkVaultName = (string)parameters["AZKVaultName"];
            string subscriptionId = (string)parameters["SubscriptionId"];

            // Ensure user is logged in to required Azure subscription.
            if (!CheckAzureSubscriptionLogIn(
                subscriptionId: subscriptionId,
                error: out error))
            {
                return false;
            }

            var results = PowerShellInvoker.InvokeScript(
                script: GetSecretScript,
                args: new object[] { name, azkVaultName },
                error: out error);

            return results.Count > 0 ? results[0].BaseObject : null;
        }

        public override bool SetSecret(
            string name, 
            object secret, 
            IReadOnlyDictionary<string, object> parameters, 
            out Exception error)
        {
            if (! (secret is SecureString))
            {
                error = new ArgumentException("The secret must be of type SecureString.");
                return false;
            }

            string azkVaultName = (string) parameters["AZKVaultName"];
            string subscriptionId = (string)parameters["SubscriptionId"];

            // Ensure user is logged in to required Azure subscription.
            if (!CheckAzureSubscriptionLogIn(
                subscriptionId: subscriptionId,
                error: out error))
            {
                return false;
            }

            // Add the secret
            PowerShellInvoker.InvokeScript(
                script: SetSecretScript,
                args: new object[] { name, secret, azkVaultName },
                error: out error);

            return (error == null);
        }

        public override bool RemoveSecret(
            string name, 
            IReadOnlyDictionary<string, object> parameters, 
            out Exception error)
        {
            string azkVaultName = (string)parameters["AZKVaultName"];
            string subscriptionId = (string)parameters["SubscriptionId"];

            // Ensure user is logged in to required Azure subscription.
            if (!CheckAzureSubscriptionLogIn(
                subscriptionId: subscriptionId,
                error: out error))
            {
                return false;
            }

            // Remove the secret
            PowerShellInvoker.InvokeScript(
                script: RemoveSecretScript,
                args: new object[] { name, azkVaultName },
                error: out error);

            return (error == null);
        }

        public override KeyValuePair<string, string>[] EnumerateSecretInfo(
            string filter,
            IReadOnlyDictionary<string, object> parameters,
            out Exception error)
        {
            string azkVaultName = (string)parameters["AZKVaultName"];
            string subscriptionId = (string)parameters["SubscriptionId"];

            // Ensure user is logged in to required Azure subscription.
            if (!CheckAzureSubscriptionLogIn(
                subscriptionId: subscriptionId,
                error: out error))
            {
                return new KeyValuePair<string, string>[0];
            }

            var results = PowerShellInvoker.InvokeScript(
                script: EnumerateSecretsScript,
                args: new object[] { filter, azkVaultName },
                error: out error);

            var list = new List<KeyValuePair<string, string>>(results.Count);
            foreach (dynamic result in results)
            {
                list.Add(
                    new KeyValuePair<string, string>(
                        key: result.Name,
                        value: nameof(SupportedTypes.SecureString)));
            }

            return list.ToArray();
        }

        #endregion

        #region Private methods

        private bool CheckAzureSubscriptionLogIn(
            string subscriptionId,
            out Exception error)
        {
            var results = PowerShellInvoker.InvokeScript(
                script: CheckSubscriptionLogIn,
                args: new object[] { subscriptionId },
                error: out Exception _);

            dynamic checkResult = results.Count > 0 ? results[0] : false;
            if (!checkResult)
            {
                var msg = string.Format(
                    CultureInfo.InstalledUICulture,
                    "To use the {0} vault, the current user needs to be logged into Azure account subscription '{1}'.  Run 'Connect-AzAccount -Subscription '{1}''",
                    VaultName,
                    subscriptionId);
                error = new InvalidOperationException(msg);
                return false;
            }

            error = null;
            return true;
        }

        #endregion

    }

    #endregion

    #region PowerShellInvoker

    internal static class PowerShellInvoker
    {
        #region Members

        // Ensure there is one instance of PowerShell per thread by using [ThreadStatic]
        // attribute to store each local thread instance.
        [ThreadStatic]
        private static System.Management.Automation.PowerShell _powerShell;

        #endregion

        #region Constructor

        static PowerShellInvoker()
        {
            _powerShell = System.Management.Automation.PowerShell.Create();
        }

        #endregion

        #region Methods

        private static void CheckPowerShell()
        {
            if ((_powerShell.InvocationStateInfo.State != PSInvocationState.Completed && _powerShell.InvocationStateInfo.State != PSInvocationState.NotStarted)
                || (_powerShell.Runspace.RunspaceStateInfo.State != RunspaceState.Opened))
            {
                _powerShell.Dispose();
                _powerShell = System.Management.Automation.PowerShell.Create();

                _powerShell = System.Management.Automation.PowerShell.Create();
                return;
            }

            _powerShell.Commands.Clear();
            _powerShell.Streams.ClearStreams();
            _powerShell.Runspace.ResetRunspaceState();
        }

        public static Collection<PSObject> InvokeScript(
            string script,
            object[] args,
            out Exception error)
        {
            CheckPowerShell();

            error = null;
            Collection<PSObject> results;
            try
            {
                results = _powerShell.AddScript(script).AddParameters(args).Invoke();
                if (_powerShell.Streams.Error.Count > 0)
                {
                    error = _powerShell.Streams.Error[0].Exception;
                }
            }
            catch (Exception ex)
            {
                error = ex;
                results = new Collection<PSObject>();
            }

            return results;
        }
    }

    #endregion

    #endregion
}
