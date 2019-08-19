// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Collections;
using System.Management.Automation;

namespace Microsoft.PowerShell.SecretsManagement
{

    #region Register-SecretsVault

    /// <summary>
    /// Cmdlet to register a secrets vaults provider module
    /// </summary>
    [Cmdlet(VerbsLifecycle.Register, "SecretsVault", DefaultParameterSetName = RegisterSecretsVaultCommand.RetrieveCmdletParameterSet)]
    public sealed class RegisterSecretsVaultCommand : PSCmdlet
    {
        #region Members

        private const string RetrieveCmdletParameterSet = "RetrieveCmdletParameterSet";
        private const string RetrieveScriptBlockParameterSet = "RetrieveScriptBlockParameterSet";

        #endregion

        #region Parameters

        /// <summary>
        /// Gets or sets a friendly name for the registered secrets vault.
        /// The name must be unique.
        /// </summary>
        [Parameter(Mandatory=true, Position=0, ParameterSetName = RegisterSecretsVaultCommand.RetrieveCmdletParameterSet)]
        [Parameter(Mandatory=true, Position=0, ParameterSetName = RegisterSecretsVaultCommand.RetrieveScriptBlockParameterSet)]
        public string Name { get; set; }

        /// <summary>
        /// Gets or sets a name of secrets vault extension module to register.
        /// </summary>
        [Parameter(Mandatory=true, Position=1, ParameterSetName = RegisterSecretsVaultCommand.RetrieveCmdletParameterSet)]
        [Parameter(Mandatory=true, Position=1, ParameterSetName = RegisterSecretsVaultCommand.RetrieveScriptBlockParameterSet)]
        public string Module { get; set; }

        /// <summary>
        /// Gets or sets the name of a cmdlet provided by the registered extension module,
        /// which provides the required parameter set for retrieving a secret.
        ///  Example:
        ///    Cmdlet Name:
        ///      Get-Secret
        ///    Required parameter set:
        ///      [string] $Name
        /// </summary>
        [Parameter(Mandatory=true, ParameterSetName = RegisterSecretsVaultCommand.RetrieveCmdletParameterSet)]
        public string SecretCmdlet { get; set; }

        /// <summary>
        /// Gets or sets a Hashtable of optional parameters to the registered vault extension module 
        /// cmdlet used to retrieve secrets from the vault.
        /// Note: This may contain sensitive information, and so should be stored in local vault.
        /// Question: How is this information updated?
        ///  Example:
        ///    -Parameters @{
        ///         SubscriptionId = "6072e8a0-4a33-4258-b158-18e2deec00fc"
        ///         TenantId = "f283eb39-32fb-4ed8-8baa-811295542bc3"
        ///         AccessToken = "1bcb7a72-579c-4f9c-a789-755d0e51ba0e"
        ///    }
        /// </summary>
        [Parameter(ParameterSetName = RegisterSecretsVaultCommand.RetrieveCmdletParameterSet)]
        public Hashtable Parameters { get; set; }

        /// <summary>
        /// Gets or sets a ScriptBlock used to retrieve a secret from the registered vault.
        /// Note: We can only store the ScriptBlock text, and this means we will need to have an internal
        ///       API that creates a ScriptBlock from an untrusted source, which in turn means that the
        ///       ScriptBlock is created in CL mode on locked down systems.
        /// </summary>
        [Parameter(ParameterSetName = RegisterSecretsVaultCommand.RetrieveScriptBlockParameterSet)]
        public ScriptBlock SecretScript { get; set; }

        /// <summary>
        /// Gets or sets a value indicating that registered secrets vault should be used as the default
        /// local vault.
        /// Question: How do we know the registered vault is local or remote?
        ///           Local vaults must support Add-Secret cmdlet or script block.
        /// </summary>
        [Parameter(ParameterSetName = RegisterSecretsVaultCommand.RetrieveCmdletParameterSet)]
        [Parameter(ParameterSetName = RegisterSecretsVaultCommand.RetrieveScriptBlockParameterSet)]
        public SwitchParameter Default { get; set; }

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
        [Parameter(Position=0, Mandatory=true)]
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
        [Parameter(Position=0)]
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
    /// Note: This will fail if the specified secrets vault doesn't exist, or doesn't have a cmdlet
    ///       for retrieving secrets (that can take splatted parameters).
    /// </summary>
    [Cmdlet(VerbsCommon.Set, "SecretsVaultParameters")]
    public sealed class SetSecretsVaultParameters : PSCmdlet
    {
        #region Parameters

        /// <summary>
        /// Gets or sets a name of secrets vault to update
        /// </summary>
        [Parameter(Position=0, Mandatory=true)]
        public string Name { get; set; }

        /// <summary>
        /// Gets or sets a hash table containing parameters to splat to the vault
        /// secrets retrieval cmdlet.
        /// </summary>
        [Parameter(Position=1, Mandatory=true)]
        public Hashtable Parameters { get; set; }

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
        [Parameter(Position=0, Mandatory=true)]
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
        [Parameter(Position=1, Mandatory=true)]
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
        [Parameter(Position=0, Mandatory=true)]
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
        [Parameter(Position=0)]
        public string Name { get; set; }

        /// <summary>
        /// Gets or sets and optional name of the vault to retrieve the secret from.
        /// If no vault name is specified then the secret will be retrieved from the default (local) vault.
        /// </summary>
        [Parameter(Position=1)]
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

}
