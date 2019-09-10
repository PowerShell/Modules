// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Management.Automation;

namespace Microsoft.PowerShell.SecretsManagement
{

    #region Register-LocalSecretsVault

    /// <summary>
    /// Cmdlet to register a local secrets vaults provider module
    /// </summary>
    [Cmdlet(VerbsLifecycle.Register, "LocalSecretsVault")]
    public sealed class RegisterLocalSecretsVaultCommand : PSCmdlet
    {
        #region Parameters

        /// <summary>
        /// Gets or sets a name for the registered local secrets vault.
        /// </summary>
        [Parameter(Mandatory=true)]
        [ValidateNotNullOrEmpty]
        public string Name { get; set; }

        /// <summary>
        /// Gets or sets a name of a secrets vault extension module to register.
        /// </summary>
        [Parameter(Mandatory=true)]
        [ValidateNotNullOrEmpty]
        public string Module { get; set; }

        /// <summary>
        /// Gets or sets a ScriptBlock used to retrieve a secret from the registered vault.
        /// Note: We can only store the ScriptBlock text, and this means we will need to have an internal
        ///       API that creates a ScriptBlock from an untrusted source, which in turn means that the
        ///       ScriptBlock is created in CL mode on locked down systems.
        /// Example:
        ///     {
        ///         param ([string] $Name)
        ///
        ///         Get-CredVaultSecret $Name
        ///     }
        /// </summary>
        [Parameter(Mandatory=true)]
        [ValidateNotNull]
        public ScriptBlock SecretGetScript { get; set; }

        /// <summary>
        /// Gets or sets a Hashtable of optional parameters to the registered vault extension module 
        /// script used to retrieve secrets from the vault.
        /// Note: This may contain sensitive information, and so should be stored in (default?) local vault.
        ///  Example:
        ///    -Parameters @{
        ///         Name = "MySecret1"
        ///         SubscriptionId = "6072e8a0-4a33-4258-b158-18e2deec00fc"
        ///         TenantId = "f283eb39-32fb-4ed8-8baa-811295542bc3"
        ///         AccessToken = "1bcb7a72-579c-4f9c-a789-755d0e51ba0e"
        ///    }
        /// </summary>
        [Parameter]
        public Hashtable SecretGetParameters { get; set; }

        /// <summary>
        /// Gets or sets a ScriptBlock used to add a secret to the registered local secrets vault.
        /// Example:
        ///     {
        ///         param([string] $Name, [object] $Secret)
        ///
        ///         Set-CredVaultSecret $Name $Secret
        ///     }
        /// </summary>
        [Parameter(Mandatory=true)]
        [ValidateNotNullOrEmpty]
        public ScriptBlock SecretAddScript { get; set; }

        /// <summary>
        /// Gets or sets a Hashtable of optional parameters to the registered vault extension module
        /// script used to retrieve secrets from the vault.
        /// Note: This may contain sensitive information, and so should be stored in (default?) local vault.
        /// Example:
        ///    -Parameters @{
        ///         Name = "MySecret1"
        ///         SubscriptionId = "6072e8a0-4a33-4258-b158-18e2deec00fc"
        ///         TenantId = "f283eb39-32fb-4ed8-8baa-811295542bc3"
        ///         AccessToken = "1bcb7a72-579c-4f9c-a789-755d0e51ba0e"
        ///    }
        /// </summary>
        [Parameter]
        public Hashtable SecretAddParameters { get; set; }

        /// <summary>
        /// Gets or sets a flag indicating that the local secrets vault should be registered and override
        /// existing local secrets vault, without confirmation.
        /// </summary>
        [Parameter]
        public SwitchParameter Force { get; set; }

        #endregion

        #region Overrides

        protected override void EndProcessing()
        {
            // TODO:
            // Read user registration data
            // Check for vault extension name conflict
            // Validate default local store (platform specific)
            // Generate local vault keys
            // Update registration data
            // Perform 'Should Process' unless -Force was selected
            // Write script and parameter data to local vault
            //   Write script block text
            //   Write parameter hash table as blob?  Or serialized text?
            //      Remove on failure (need transaction semantics)
            // Open registration file exclusively
            // Write data
            //      Update PowerShell object to take 'untrusted input'
            //      Find out how to access credman via C# (PInvoke?)
            //          ** Need to create PInvoke API for CredWriteA() and CredReadA() APIs (Windows only) **
            //          ** Experiment with writing/reading scriptblock and param dictionary. **
            //      Add support for Linux (Gnome Keyring)
        }

        #endregion

    }

    #endregion

    #region Register-RemoteSecretsVault

    /// <summary>
    /// Cmdlet to register a remote secrets vaults provider module
    /// </summary>
    [Cmdlet(VerbsLifecycle.Register, "RemoteSecretsVault")]
    public sealed class RegisterRemoteSecretsVaultCommand : PSCmdlet
    {
        #region Parameters

        /// <summary>
        /// Gets or sets a friendly name for the registered secrets vault.
        /// The name must be unique.
        /// </summary>
        [Parameter(Mandatory=true)]
        [ValidateNotNullOrEmpty]
        public string Name { get; set; }

        /// <summary>
        /// Gets or sets a name of a secrets vault extension module to register.
        /// </summary>
        [Parameter(Mandatory=true)]
        [ValidateNotNullOrEmpty]
        public string Module { get; set; }

        /// <summary>
        /// Gets or sets a ScriptBlock used to retrieve a secret from the registered vault.
        /// Note: We can only store the ScriptBlock text, and this means we will need to have an internal
        ///       API that creates a ScriptBlock from an untrusted source, which in turn means that the
        ///       ScriptBlock is created in CL mode on locked down systems.
        /// Example:
        ///     {
        ///         param ([string] $Name)
        ///
        ///         Get-AzSecret $Name
        ///     }
        /// </summary>
        [Parameter(Mandatory=true)]
        [ValidateNotNull]
        public ScriptBlock SecretGetScript { get; set; }

        /// <summary>
        /// Gets or sets a Hashtable of optional parameters to the registered vault extension module 
        /// script used to retrieve secrets from the vault.
        /// Note: This may contain sensitive information, and so should be stored in local vault.
        /// Question: How is this information updated?
        ///  Example:
        ///    -Parameters @{
        ///         Name = "MySecret1"
        ///         SubscriptionId = "6072e8a0-4a33-4258-b158-18e2deec00fc"
        ///         TenantId = "f283eb39-32fb-4ed8-8baa-811295542bc3"
        ///         AccessToken = "1bcb7a72-579c-4f9c-a789-755d0e51ba0e"
        ///    }
        /// </summary>
        [Parameter]
        public Hashtable SecretGetParameters { get; set; }

        #endregion

        #region Overrides

        protected override void ProcessRecord()
        {
            // TODO: 
        }

        #endregion

    }

    #endregion

    #region Unregister-SecretsVault

    /// <summary>
    /// Cmdlet to unregister a secrets vault.
    /// </summary>
    [Cmdlet(VerbsLifecycle.Unregister, "SecretsVault")]
    public sealed class UnregisterSecretsVaultCommand : PSCmdlet
    {
        #region Parameters

        /// <summary>
        /// Gets or sets a name of the secrets vault to unregister.
        /// </summary>
        [Parameter(Mandatory=true)]
        [ValidateNotNullOrEmpty]
        public string Name { get; set; }

        #endregion

        #region Overrides

        protected override void ProcessRecord()
        {
            // TODO:
        }

        #endregion
    }

    #endregion

    #region Get-SecretsVault

    /// <summary>
    /// Class that contains secret vault information.
    /// </summary>
    public sealed class SecretVaultInfo
    {
        // TODO:
        //  Name
        //  Module (name)
        //  Get cmdlet
        //  Get scriptblock
        //  ...
    }

    /// <summary>
    /// Cmdlet to return registered secret vaults as SecretVaultInfo objects.
    /// If no name is provided then all registered secret vaults will be returned.
    /// </summary>
    [Cmdlet(VerbsCommon.Get, "SecretsVault")]
    public sealed class GetSecretsVaultCommand : PSCmdlet
    {
        #region Parameters

        /// <summary>
        /// Gets or sets an optional name of the secret vault to return.
        /// <summary>
        [Parameter]
        public string Name { get; set; }

        #endregion

        #region Overrides

        protected override void ProcessRecord()
        {
            // TODO:
        }

        #endregion
    }

    #endregion

    #region Set-SecretsVaultParameters

    /// <summary>
    /// Cmdlet to set the parameters hash table for an existing registered vault.
    /// Note: This will fail if the specified secrets vault doesn't exist.
    /// </summary>
    [Cmdlet(VerbsCommon.Set, "SecretsVaultParameters")]
    public sealed class SetSecretsVaultParameters : PSCmdlet
    {
        #region Parameters

        /// <summary>
        /// Gets or sets a name of secrets vault extension to update
        /// </summary>
        [Parameter(Mandatory=true)]
        [ValidateNotNullOrEmpty]
        public string Name { get; set; }

        /// <summary>
        /// Gets or sets a hash table containing parameters to splat to the vault
        /// secrets retrieval cmdlet.
        /// </summary>
        [Parameter(Mandatory=true)]
        [ValidateNotNullOrEmpty]
        public Hashtable Parameters { get; set; }

        [Parameter]
        [ValidateSet("Get","Add")]
        public string ParamType { get; set; } = "Get";

        #endregion

        #region Overrides

        protected override void ProcessRecord()
        {
            // TODO: 
        }

        #endregion
    }

    #endregion

    #region Add-Secret

    /// <summary>
    /// Adds a provided secret to the local default vault.
    /// </summary>
    [Cmdlet(VerbsCommon.Add, "Secret")]
    public sealed class AddSecretCommand : PSCmdlet
    {
        #region Parameters

        /// <summary>
        /// Gets or sets a name of the secret to be added.
        /// </summary>
        [Parameter(Mandatory=true)]
        [ValidateNotNullOrEmpty]
        public string Name { get; set; }

        /// <summary>
        /// Gets or sets a value that is the secret to be added.
        /// Supported types:
        ///     PSCredential
        ///     SecureString
        ///     String
        ///     Hashtable
        ///     byte[]
        /// </summary>
        [Parameter(Mandatory=true)]
        public object Secret { get; set; }

        /// <summary>
        /// Gets or sets a flag indicating whether an existing secret with the same name is overwritten.
        /// </summary>
        [Parameter]
        public SwitchParameter NoClobber { get; set; }

        #endregion

        #region Overrides

        protected override void ProcessRecord()
        {
            // TODO: 
        }

        #endregion

    }

    #endregion

    #region Remove-Secret

    /// <summary>
    /// Removes a secret by name from the local default vault.
    /// <summary>
    [Cmdlet(VerbsCommon.Remove, "Secret")]
    public sealed class RemoveSecretCommand : PSCmdlet
    {
        #region Parameters

        /// <summary>
        /// Gets or sets a name of the secret to be removed.
        /// </summary>
        [Parameter(Mandatory=true)]
        [ValidateNotNullOrEmpty]
        public string Name { get; set; }

        #endregion

        #region Overrides

        protected override void ProcessRecord()
        {
            // TODO: 
        }

        #endregion

    }

    #endregion

    #region Get-Secret

    /// <summary>
    /// Retrieves a secret by name.
    /// If no name is provided then returns all secrets from vault.
    /// </summary>
    [Cmdlet(VerbsCommon.Get, "Secret")]
    [OutputType(typeof(object))]
    public sealed class GetSecretCommand : PSCmdlet
    {
        #region Parameters

        /// <summary>
        /// Gets or sets an optional name of secret to retrieve.
        [Parameter]
        public string Name { get; set; }

        /// <summary>
        /// Gets or sets an optional name of the vault to retrieve the secret from.
        /// If no vault name is specified then the secret will be retrieved from the default (local) vault.
        /// </summary>
        [Parameter]
        public string Vault { get; set; }

        #endregion

        #region Overrides

        protected override void ProcessRecord()
        {
            // TODO: 
        }

        #endregion
    }

    #endregion

    #region Test Only

    [Cmdlet(VerbsCommon.Add, "LocalSecret")]
    public sealed class AddLocalSecretCommand : PSCmdlet
    {
        [Parameter(Position=0, Mandatory=true)]
        [ValidateNotNullOrEmpty]
        public string Name { get; set; }

        [Parameter(Position=1, Mandatory=true)]
        [ValidateNotNullOrEmpty]
        public PSObject Secret { get; set; }

        protected override void EndProcessing()
        {
            try
            {
                int errorCode = 0;
                if (!CredMan.WriteObject(
                    Name,
                    Secret.BaseObject,
                    ref errorCode))
                {
                    var msg = CredMan.GetErrorMessage(errorCode);
                    WriteError(
                        new ErrorRecord(
                            new System.InvalidOperationException(
                                string.Format(CultureInfo.InvariantCulture, "Failed to write secret {0} to vault, with error: {1}", Name, msg)),
                                "SecretWriteError",
                                ErrorCategory.InvalidOperation,
                                this));
                }
            }
            catch (System.Exception ex)
            {
                WriteError(
                    new ErrorRecord(
                        ex,
                        "SecretWriteError",
                        ErrorCategory.InvalidOperation,
                        this));
            }
        }
    }

    [Cmdlet(VerbsCommon.Get, "LocalSecret")]
    public sealed class GetLocalSecretCommand : PSCmdlet
    {
        [Parameter(Position=0)]
        public string Name { get; set; }

        protected override void EndProcessing()
        {
            int errorCode = 0;
            string msg = null;

            if (!string.IsNullOrEmpty(Name) && (Name.IndexOf('*') < 0))
            {
                if (CredMan.ReadObject(
                    Name,
                    out object outObject,
                    ref errorCode))
                {
                    WriteObject(outObject);
                    return;
                }

                msg = CredMan.GetErrorMessage(errorCode);
                WriteError(
                    new ErrorRecord(
                        new System.InvalidOperationException(
                            string.Format(CultureInfo.InvariantCulture, "Failed to read secret {0} from vault, with error: {1}", Name, msg)),
                            "SecretWriteError",
                            ErrorCategory.InvalidOperation,
                            this));

                return;
            }

            // Enumerate allowing wildcards in name, used as a filter parameter.
            var filter = Name?? "*";
            if (CredMan.EnumerateObjects(
                filter,
                out KeyValuePair<string, object>[] outObjects,
                ref errorCode))
            {
                WriteObject(outObjects);
                return;
            }

            msg = CredMan.GetErrorMessage(errorCode);
            WriteError(
                new ErrorRecord(
                    new System.InvalidOperationException(
                        string.Format(CultureInfo.InvariantCulture, "Failed to find secrets {0} in vault, with error: {1}", Name, msg)),
                        "SecretWriteError",
                        ErrorCategory.InvalidOperation,
                        this));
        }
    }

    [Cmdlet(VerbsCommon.Remove, "LocalSecret")]
    public sealed class RemoveLocalSecretCommand : PSCmdlet
    {
        [Parameter(Position=0, Mandatory=true)]
        public string Name { get; set; }

        protected override void EndProcessing()
        {
            int errorCode = 0;
            if (!CredMan.ReadObject(
                Name,
                out object outObject,
                ref errorCode))
            {
                WriteRemoveError(errorCode);
                return;
            }

            switch (outObject)
            {
                case Hashtable hashtable:
                    if (!CredMan.DeleteHashtable(
                        Name,
                        ref errorCode))
                    {
                        WriteRemoveError(errorCode);
                    }
                    break;

                default:
                    if (!CredMan.DeleteObject(
                        Name,
                        ref errorCode))
                    {
                        WriteRemoveError(errorCode);
                    }
                    break;
            }
        }

        private void WriteRemoveError(int errorCode)
        {
                var msg = CredMan.GetErrorMessage(errorCode);
                WriteError(
                    new ErrorRecord(
                        new System.InvalidOperationException(
                            string.Format(CultureInfo.InvariantCulture, "Failed to remove secret {0} in vault, with error: {1}", Name, msg)),
                            "SecretWriteError",
                            ErrorCategory.InvalidOperation,
                            this));
        }
    }

    #endregion
}
