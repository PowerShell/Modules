// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Management.Automation;

using Dbg = System.Diagnostics.Debug;

namespace Microsoft.PowerShell.SecretsManagement
{
    #region SecretsVaultInfo

    /// <summary>
    /// Class that contains secret vault information.
    /// </summary>
    public sealed class SecretsVaultInfo
    {
        #region Parameters

        /// <summary>
        /// Gets name of extension vault.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets name of extension vault module.
        /// </summary>
        public string ModuleName { get; }

        /// <summary>
        /// Gets extension vault module path.
        /// </summary>
        public string ModulePath { get; }

        /// <summary>
        /// Gets boolean indicating if module supports Get-Secret cmdlet.
        /// </summary>
        public bool HaveGetCmdlet { get; }

        /// <summary>
        /// Gets boolean indicating if module supports Set-Secret cmdlet.
        /// </summary>
        public bool HaveSetCmdlet { get; }

        /// <summary>
        /// Gets boolean indicating if module supports Remove-Secret cmdlet.
        /// </summary>
        public bool HaveRemoveCmdlet { get; }

        /// <summary>
        /// Gets optional script that performs Get-Secret function.
        /// </summary>
        public string GetSecretScript { get; }

        /// <summary>
        /// Gets optional script parameters for the Get-Secret script.
        /// </summary>
        public string GetSecretParamsName { get; }

        /// <summary>
        /// Gets optional script that performs Set-Secret function.
        /// </summary>
        public string SetSecretScript { get; }

        /// <summary>
        /// Gets optional script parameters for Set-Secret script.
        /// </summary>
        public string SetSecretParamsName { get; }

        /// <summary>
        /// Gets optional script that performs Remove-Secret function.
        /// </summary>
        public string RemoveSecretScript { get; }

        /// <summary>
        /// Gets optional script parameters for Remove-Secret script.
        /// </summary>
        public string RemoveSecretParamsName { get; }

        #endregion

        #region Constructor

        internal SecretsVaultInfo(
            string name,
            ExtensionVaultModule vaultInfo)
        {
            Name = name;
            ModuleName = vaultInfo.ModuleName;
            ModulePath = vaultInfo.ModulePath;
            HaveGetCmdlet = vaultInfo.HaveGetCommand;
            HaveSetCmdlet = vaultInfo.HaveSetCommand;
            HaveRemoveCmdlet = vaultInfo.HaveRemoveCommand;
            GetSecretScript = vaultInfo.GetSecretScript;
            GetSecretParamsName = vaultInfo.GetSecretParamsName;
            SetSecretScript = vaultInfo.SetSecretScript;
            SetSecretParamsName = vaultInfo.SetSecretParamsName;
            RemoveSecretScript = vaultInfo.RemoveSecretScript;
            RemoveSecretParamsName = vaultInfo.RemoveSecretParamsName;
        }

        #endregion
    }

    #endregion

    #region Register-SecretsVault

    /// <summary>
    /// Cmdlet to register a remote secrets vaults provider module
    /// </summary>
    [Cmdlet(VerbsLifecycle.Register, "SecretsVault", SupportsShouldProcess = true)]
    public sealed class RegisterSecretsVaultCommand : PSCmdlet
    {
        #region Members

        internal const string ScriptParamTag = "_SPT_";
        internal const string BuiltInLocalVault = "BuiltInLocalVault";

        #endregion

        #region Parameters

        /// <summary>
        /// Gets or sets a friendly name for the registered secrets vault.
        /// The name must be unique.
        /// </summary>
        [Parameter(Position=0, Mandatory=true)]
        [ValidateNotNullOrEmpty]
        public string Name { get; set; }

        /// <summary>
        /// Gets or sets the path of the vault extension module to register.
        /// </summary>
        [Parameter(Position=1, Mandatory=true)]
        [ValidateNotNullOrEmpty]
        public string ModulePath { get; set; }

        /// <summary>
        /// Gets or sets an optional ScriptBlock used to retrieve a secret from the registered vault.
        /// Note: We can only store the ScriptBlock text, and this means we will need to have an internal
        ///       API that creates a ScriptBlock from an untrusted source, which in turn means that the
        ///       ScriptBlock is created in CL mode on locked down systems.
        /// </summary>
        [Parameter]
        [ValidateNotNull]
        public ScriptBlock GetSecretScript { get; set; }

        /// <summary>
        /// Gets or sets an optional Hashtable of parameters to the registered vault extension module 
        /// script used to retrieve secrets from the vault.
        /// Note: This may contain sensitive information, and so should be stored in local vault.
        /// </summary>
        [Parameter]
        public Hashtable GetSecretParameters { get; set; }

        /// <summary>
        /// Gets or sets an optional ScriptBlock used to add a secret to the registered vault.
        /// Note: Add script block text only and convert to ScriptBlock (safely) as needed.
        /// </summary>
        [Parameter]
        [ValidateNotNull]
        public ScriptBlock SetSecretScript { get; set; }

        /// <summary>
        /// Gets or sets an optional Hashtable of parameters to the registered vault extension module
        /// script used to add secrets to the vault.
        /// </summary>
        [Parameter]
        public Hashtable SetSecretParameters { get; set; }

        /// <summary>
        /// Gets or sets an optional ScriptBlock used to remove a secret from the registered vault.
        /// </summary>
        [Parameter]
        [ValidateNotNull]
        public ScriptBlock RemoveSecretScript { get; set; }

        /// <summary>
        /// Gets or sets an optional Hashtable of parameters to the registered vault extension module
        /// script used to remove secrets from the vault.
        /// </summary>
        [Parameter]
        public Hashtable RemoveSecretParameters { get; set; }

        #endregion

        #region Overrides

        protected override void BeginProcessing()
        {
            if (Name.Equals(BuiltInLocalVault, StringComparison.OrdinalIgnoreCase))
            {
                var msg = string.Format(CultureInfo.InvariantCulture, 
                    "The name {0} is reserved and cannot be used for a vault extension.", BuiltInLocalVault);
                ThrowTerminatingError(
                    new ErrorRecord(
                        new PSArgumentException(msg),
                        "RegisterSecretsVaultInvalidName",
                        ErrorCategory.InvalidArgument,
                        this));
            }
        }

        protected override void EndProcessing()
        {
            var vaultInfo = new Hashtable();

            // Validate mandatory parameters.
            var vaultItems = RegisteredVaultCache.GetAll();
            if (vaultItems.ContainsKey(Name))
            {
                ThrowTerminatingError(
                    new ErrorRecord(
                        new InvalidOperationException("Provided Name for vault is already being used."),
                        "RegisterSecretsVaultInvalidVaultName",
                        ErrorCategory.InvalidArgument,
                        this));
            }

            var filePaths = this.GetResolvedProviderPathFromPSPath(ModulePath, out ProviderInfo _);
            if (filePaths.Count == 0)
            {
                ThrowTerminatingError(
                    new ErrorRecord(
                        new PSArgumentException("Provided ModulePath is not valid."),
                        "RegisterSecretsVaultInvalidModulePath",
                        ErrorCategory.InvalidArgument,
                        this));
            }
            ModulePath = filePaths[0];
            vaultInfo.Add(ExtensionVaultModule.ModulePathStr, ModulePath);

            if (!ShouldProcess(Name, VerbsLifecycle.Register))
            {
                return;
            }

            // Get module information (don't load it because binary modules cannot be re-loaded).
            var results = InvokeCommand.InvokeScript(
                script: @"
                    param ([string] $ModulePath)

                    Get-Module -Name $ModulePath -List
                ",
                args: new object[] { ModulePath });
            PSModuleInfo moduleInfo = (results.Count == 1) ? results[0].BaseObject as PSModuleInfo : null;
            if (moduleInfo == null)
            {
                ThrowTerminatingError(
                    new ErrorRecord(
                        new PSInvalidOperationException("Could not retrieve module information."),
                        "RegisterSecretsVaultCantGetModuleInfo",
                        ErrorCategory.InvalidOperation,
                        this));
            }
            vaultInfo.Add(ExtensionVaultModule.ModuleNameStr, moduleInfo.Name);

            // Check for supported cmdlets
            bool hasGet = moduleInfo.ExportedCommands.ContainsKey(ExtensionVaultModule.DefaultGetSecretCmd);
            vaultInfo.Add(ExtensionVaultModule.HaveGetCmdletStr, hasGet);
            bool hasSet = moduleInfo.ExportedCommands.ContainsKey(ExtensionVaultModule.DefaultSetSecretCmd);
            vaultInfo.Add(ExtensionVaultModule.HaveSetCmdletStr, hasSet);
            bool hasRemove = moduleInfo.ExportedCommands.ContainsKey(ExtensionVaultModule.DefaultRemoveSecretCmd);
            vaultInfo.Add(ExtensionVaultModule.HaveRemoveCmdletStr, hasRemove);

            // Sanity check for required Get-Secret.
            if (!hasGet && GetSecretScript == null)
            {
                ThrowTerminatingError(
                    new ErrorRecord(
                        new PSArgumentException("The required Get-Secret cmdlet or script block was not found."),
                        "RegisterSecretsVaultMissingGetSecret",
                        ErrorCategory.InvalidArgument,
                        this));
            }

            vaultInfo.Add(
                key: ExtensionVaultModule.GetSecretScriptStr, 
                value: GetSecretScript?.ToString());

            StoreSecretParameters(
                vaultInfo: vaultInfo,
                key: ExtensionVaultModule.GetSecretParamsStr,
                parameters: GetSecretParameters);

            vaultInfo.Add(
                key: ExtensionVaultModule.SetSecretScriptStr, 
                value: SetSecretScript?.ToString());

            StoreSecretParameters(
                vaultInfo: vaultInfo,
                key: ExtensionVaultModule.SetSecretParamsStr,
                parameters: SetSecretParameters);

            vaultInfo.Add(
                key: ExtensionVaultModule.RemoveSecretScriptStr,
                value: RemoveSecretScript?.ToString());

            StoreSecretParameters(
                vaultInfo: vaultInfo,
                key: ExtensionVaultModule.RemoveSecretParamsStr,
                parameters: RemoveSecretParameters);

            // Register new secret vault information.
            RegisteredVaultCache.Add(
                keyName: Name,
                vaultInfo: vaultInfo);
        }

        #endregion

        #region Private methods

        private void StoreSecretParameters(
            Hashtable vaultInfo,
            string key,
            Hashtable parameters)
        {
            var parametersName = string.Empty;

            if (parameters != null)
            {
                // Generate unique name for parameters based on vault name.
                parametersName = ScriptParamTag + Name + "_" + key + "_";

                // Store parameters in built-in local secure vault.
                int errorCode = 0;
                if (!LocalSecretStore.WriteObject(
                    name: parametersName,
                    parameters,
                    ref errorCode))
                {
                    var msg = string.Format(
                        CultureInfo.InvariantCulture, 
                        "Unable to register vault extension because writing script parameters to the built-in local store failed with error: {0}",
                        LocalSecretStore.GetErrorMessage(errorCode));

                    ThrowTerminatingError(
                        new ErrorRecord(
                            new PSInvalidOperationException(msg),
                            "RegisterSecretsVaultCannotSaveParameters",
                            ErrorCategory.WriteError,
                            this));
                }
            }
            
            // Add parameters store name to the vault registry information.
            vaultInfo.Add(
                key: key,
                value: parametersName);
        }

        #endregion
    }

    #endregion

    #region Unregister-SecretsVault

    /// <summary>
    /// Cmdlet to unregister a secrets vault.
    /// </summary>
    [Cmdlet(VerbsLifecycle.Unregister, "SecretsVault", SupportsShouldProcess = true)]
    public sealed class UnregisterSecretsVaultCommand : PSCmdlet
    {
        #region Parameters

        private const string NameParameterSet = "NameParameterSet";
        private const string SecretsVaultParameterSet = "SecretsVaultParameterSet";

        /// <summary>
        /// Gets or sets a name of the secrets vault to unregister.
        /// </summary>
        [Parameter(ParameterSetName = NameParameterSet,
                   Position = 0, 
                   Mandatory = true)]
        [ValidateNotNullOrEmpty]
        public string Name { get; set; }

        [Parameter(ParameterSetName = SecretsVaultParameterSet,
                   Position = 0,
                   Mandatory = true,
                   ValueFromPipeline = true,
                   ValueFromPipelineByPropertyName = true)]
        [ValidateNotNull]
        public SecretsVaultInfo SecretsVault { get; set; }

        #endregion

        #region Overrides

        /// <summary>
        /// Process input
        /// </summary>
        protected override void ProcessRecord()
        {
            if (!ShouldProcess(Name, VerbsLifecycle.Unregister))
            {
                return;
            }

            string vaultName;
            switch (ParameterSetName)
            {
                case NameParameterSet:
                    vaultName = Name;
                    break;
                
                case SecretsVaultParameterSet:
                    vaultName = SecretsVault.Name;
                    break;

                default:
                    Dbg.Assert(false, "Invalid parameter set");
                    vaultName = string.Empty;
                    break;
            }

            var removedVaultInfo = RegisteredVaultCache.Remove(vaultName);
            if (removedVaultInfo == null)
            {
                var msg = string.Format(CultureInfo.InvariantCulture,
                    "Unable to find secrets vault {0} to unregister it.", vaultName);
                WriteError(
                    new ErrorRecord(
                        new ItemNotFoundException(msg),
                        "UnregisterSecretsVaultObjectNotFound",
                        ErrorCategory.ObjectNotFound,
                        this));

                return;
            }

            // Remove any parameter secrets from built-in local store.
            RemoveParamSecrets(removedVaultInfo, ExtensionVaultModule.GetSecretParamsStr);
            RemoveParamSecrets(removedVaultInfo, ExtensionVaultModule.SetSecretParamsStr);
            RemoveParamSecrets(removedVaultInfo, ExtensionVaultModule.RemoveSecretParamsStr);
        }

        #endregion

        #region Private methods

        private void RemoveParamSecrets(
            Hashtable vaultInfo,
            string ParametersNameKey)
        {
            if (vaultInfo != null && vaultInfo.ContainsKey(ParametersNameKey))
            {
                var parametersName = (string) vaultInfo[ParametersNameKey];
                if (!string.IsNullOrEmpty(parametersName))
                {
                    int errorCode = 0;
                    if (!LocalSecretStore.DeleteObject(parametersName, ref errorCode))
                    {
                        var errorMessage = LocalSecretStore.GetErrorMessage(errorCode);
                        var msg = string.Format(CultureInfo.InvariantCulture, 
                            "Removal of vault info script parameters {0} failed with error {1}", parametersName, errorMessage);
                        WriteError(
                            new ErrorRecord(
                                new PSInvalidOperationException(msg),
                                "UnregisterSecretsVaultRemoveScriptParametersFailed",
                                ErrorCategory.InvalidOperation,
                                this));
                    }
                }
            }
        }

        #endregion
    }

    #endregion

    #region SecretsCmdlet

    public abstract class SecretsCmdlet : PSCmdlet
    {
        /// <summary>
        /// Look up and return specified extension module by name.
        /// </summary>
        /// <param name="name">Name of extension vault to return.</param>
        /// <returns>Extension vault.</returns>
        internal ExtensionVaultModule GetExtensionVault(string name)
        {
            if (!RegisteredVaultCache.VaultExtensions.TryGetValue(
                    key: name,
                    value: out ExtensionVaultModule extensionModule))
                {
                    var msg = string.Format(CultureInfo.InvariantCulture, "Vault not found in registry: {0}", name);
                    ThrowTerminatingError(
                        new ErrorRecord(
                            new PSInvalidOperationException(msg),
                            "GetSecretVaultNotFound",
                            ErrorCategory.ObjectNotFound,
                            this));
                }

            return extensionModule;
        }

        internal void WriteInvokeErrors(PSDataCollection<ErrorRecord> errors)
        {
            foreach (var error in errors)
            {
                WriteVerbose(error.ToString());
            }
        }
    }

    #endregion

    #region Get-SecretsVault

    /// <summary>
    /// Cmdlet to return registered secret vaults as SecretsVaultInfo objects.
    /// If no name is provided then all registered secret vaults will be returned.
    /// </summary>
    [Cmdlet(VerbsCommon.Get, "SecretsVault")]
    [OutputType(typeof(SecretsVaultInfo))]
    public sealed class GetSecretsVaultCommand : SecretsCmdlet
    {
        #region Parameters

        /// <summary>
        /// Gets or sets an optional name of the secret vault to return.
        /// <summary>
        [Parameter(Position=0)]
        public string Name { get; set; }

        #endregion

        #region Overrides

        protected override void EndProcessing()
        {
            var namePattern = new WildcardPattern(
                (!string.IsNullOrEmpty(Name)) ? Name : "*", 
                WildcardOptions.IgnoreCase);

            var vaultExtensions = RegisteredVaultCache.VaultExtensions;
            foreach (var vaultName in vaultExtensions.Keys)
            {
                if (namePattern.IsMatch(vaultName))
                {
                    if (vaultExtensions.TryGetValue(vaultName, out ExtensionVaultModule extensionModule))
                    {
                        WriteObject(
                            new SecretsVaultInfo(
                                vaultName,
                                extensionModule));
                    }
                }
            }
        }

        #endregion
    }

    #endregion

    #region Get-Secrets

    /// <summary>
    /// Enumerates secrets by name, wild cards are allowed.
    /// If no name is provided then all secrets are returned.
    /// If no vault is specified then all vaults are searched.
    /// </summary>
    [Cmdlet(VerbsCommon.Get, "Secrets")]
    [OutputType(typeof(PSObject))]
    public sealed class GetSecretsCommand : SecretsCmdlet
    {
        #region Parameters

        /// <summary>
        /// Gets or sets a name used to match and return secrets.
        /// </summary>
        [Parameter(Position=0)]
        public string Name { get; set; }

        /// <summary>
        /// Gets or sets an optional name of the vault to retrieve the secret from.
        /// </summary>
        [Parameter(Position=1)]
        public string Vault { get; set; }

        /// <summary>
        /// Gets or sets an optional switch that lists all local secrets, including registered vault parameter sets.
        /// </summary>
        [Parameter]
        public SwitchParameter AllLocal { get; set; }

        #endregion

        #region Overrides

        protected override void EndProcessing()
        {
            if (string.IsNullOrEmpty(Name))
            {
                Name = "*";
            }

            // Search single vault, if provided.
            if (!string.IsNullOrEmpty(Vault))
            {
                if (Vault.Equals(RegisterSecretsVaultCommand.BuiltInLocalVault, StringComparison.OrdinalIgnoreCase))
                {
                    SearchLocalStore(Name);
                    return;
                }

                var extensionModule = GetExtensionVault(Vault);
                WriteObject(
                    extensionModule.InvokeGetSecret(
                        name: Name,
                        errors: out PSDataCollection<ErrorRecord> errors));
                WriteInvokeErrors(errors);

                return;
            }

            // Search through all extension vaults.
            foreach (var extensionModule in RegisteredVaultCache.VaultExtensions.Values)
            {
                try
                {
                    WriteObject(
                        extensionModule.InvokeGetSecret(
                            name: Name,
                            errors: out PSDataCollection<ErrorRecord> errors));
                    WriteInvokeErrors(errors);
                }
                catch (Exception ex)
                {
                    WriteError(
                        new ErrorRecord(
                            ex,
                            "GetSecretException",
                            ErrorCategory.InvalidOperation,
                            this));
                }
            }

            // Lastly, search the local built in store.
            SearchLocalStore(Name);
        }

        #endregion

        #region Private methods

        private void SearchLocalStore(string name)
        {
            // Search through the built-in local vault.
            int errorCode = 0;
            if (LocalSecretStore.EnumerateObjects(
                filter: Name,
                all: false,
                outObjects: out KeyValuePair<string, object>[] outObjects,
                errorCode: ref errorCode))
            {
                foreach (var pair in outObjects)
                {
                    // Filter out the extension vault script block parameter sets.
                    if (!AllLocal &&
                        pair.Key.StartsWith(RegisterSecretsVaultCommand.ScriptParamTag))
                    {
                        continue;
                    }

                    var psObject = new PSObject();
                    psObject.Members.Add(
                        new PSNoteProperty(
                            "Name", 
                            pair.Key));
                    psObject.Members.Add(
                        new PSNoteProperty(
                            "Value",
                            pair.Value));
                    psObject.Members.Add(
                        new PSNoteProperty(
                            "Vault",
                            RegisterSecretsVaultCommand.BuiltInLocalVault));

                    WriteObject(psObject);
                }
            }
        }

        #endregion
    }

    #endregion

    #region Get-Secret

    /// <summary>
    /// Retrieves a secret by name, wild cards are allowed.
    /// If no vault is specified then all vaults are searched.
    /// The first secret matching the Name parameter is returned.
    /// </summary>
    [Cmdlet(VerbsCommon.Get, "Secret")]
    [OutputType(typeof(object))]
    public sealed class GetSecretCommand : SecretsCmdlet
    {
        #region Parameters

        /// <summary>
        /// Gets or sets a name of secret to retrieve.
        /// <summary>
        [Parameter(Position=0, Mandatory=true)]
        public string Name { get; set; }

        /// <summary>
        /// Gets or sets an optional name of the vault to retrieve the secret from.
        /// </summary>
        [Parameter(Position=1)]
        public string Vault { get; set; }

        #endregion

        #region Overrides

        protected override void EndProcessing()
        {
            // Search single vault.
            if (!string.IsNullOrEmpty(Vault))
            {
                if (Vault.Equals(RegisterSecretsVaultCommand.BuiltInLocalVault, StringComparison.OrdinalIgnoreCase))
                {
                    SearchLocalStore(Name);
                    return;
                }

                var extensionModule = GetExtensionVault(Vault);
                var results = extensionModule.InvokeGetSecret(
                    name: Name,
                    errors: out PSDataCollection<ErrorRecord> errors);
                if (results.Count > 0)
                {
                    dynamic secret = results[0];
                    WriteObject(secret.Value);
                }
                WriteInvokeErrors(errors);
                return;
            }

            // Search through all vaults.
            foreach (var extensionModule in RegisteredVaultCache.VaultExtensions.Values)
            {
                try
                {
                    var results = extensionModule.InvokeGetSecret(
                        name: Name,
                        errors: out PSDataCollection<ErrorRecord> errors);
                    if (results.Count > 0)
                    {
                        dynamic secret = results[0];
                        if (secret != null)
                        {
                            WriteObject(secret.Value);
                        }
                        return;
                    }
                    WriteInvokeErrors(errors);
                }
                catch (Exception ex)
                {
                    WriteError(
                        new ErrorRecord(
                            ex,
                            "GetSecretException",
                            ErrorCategory.InvalidOperation,
                            this));
                }
            }

            // Finally search the built-in local vault.
            if (SearchLocalStore(Name))
            {
                return;
            }

            var msg = string.Format(CultureInfo.InvariantCulture, "The secret {0} was not found.", Name);
            WriteError(
                new ErrorRecord(
                    new ItemNotFoundException(msg),
                    "GetSecretNotFound",
                    ErrorCategory.ObjectNotFound,
                    this));
        }

        #endregion

        #region Private methods

        private bool SearchLocalStore(string name)
        {
            int errorCode = 0;
            if (LocalSecretStore.ReadObject(
                name: name,
                outObject: out object outObject,
                ref errorCode))
            {
                WriteObject(outObject);
                return true;
            }

            return false;
        }

        #endregion
    }

    #endregion

    #region Add-Secret

    /// <summary>
    /// Adds a provided secret to the specified extension vault, 
    /// or the built-in default store if an extension vault is not specified.
    /// </summary>
    [Cmdlet(VerbsCommon.Add, "Secret")]
    public sealed class AddSecretCommand : SecretsCmdlet
    {
        #region Parameters

        /// <summary>
        /// Gets or sets a name of the secret to be added.
        /// </summary>
        [Parameter(Position=0, Mandatory=true)]
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
        [Parameter(Position=1, Mandatory=true, ValueFromPipeline=true)]
        public object Secret { get; set; }

        /// <summary>
        /// Gets or sets an optional extension vault name.
        /// </summary>
        [Parameter(Position=2)]
        public string Vault { get; set; }

        /// <summary>
        /// Gets or sets a flag indicating whether an existing secret with the same name is overwritten.
        /// </summary>
        [Parameter]
        public SwitchParameter NoClobber { get; set; }

        #endregion

        #region Overrides

        protected override void EndProcessing()
        {
            // Add to specified vault.
            if (!string.IsNullOrEmpty(Vault))
            {
                var extensionModule = GetExtensionVault(Vault);
                
                // If NoClobber is selected, then check to see if it already exists.
                if (NoClobber)
                {
                    var results = extensionModule.InvokeGetSecret(
                        name: Name,
                        errors: out PSDataCollection<ErrorRecord> _);
                    if (results.Count > 0)
                    {
                        var msg = string.Format(CultureInfo.InvariantCulture, 
                            "A secret with name {0} already exists in vault {1}", Name, Vault);
                        ThrowTerminatingError(
                            new ErrorRecord(
                                new PSInvalidOperationException(msg),
                                "AddSecretAlreadyExists",
                                ErrorCategory.ResourceExists,
                                this));
                    }
                }

                // Add new secret to vault.
                extensionModule.InvokeSetSecret(
                    cmdlet: this,
                    name: Name,
                    secret: Secret);
                
                return;
            }

            // Add to default built-in vault (after NoClobber check).
            int errorCode = 0;
            if (NoClobber)
            {
                if (LocalSecretStore.ReadObject(
                    name: Name,
                    out object _,
                    ref errorCode))
                {
                    var msg = string.Format(CultureInfo.InvariantCulture, 
                        "A secret with name {0} already exists in the local default vault", Name);
                    ThrowTerminatingError(
                        new ErrorRecord(
                            new PSInvalidOperationException(msg),
                            "AddSecretAlreadyExists",
                            ErrorCategory.ResourceExists,
                            this));
                }
            }

            errorCode = 0;
            if (!LocalSecretStore.WriteObject(
                name: Name,
                objectToWrite: (Secret is PSObject psObject) ? psObject.BaseObject : Secret,
                ref errorCode))
            {
                var errorMessage = LocalSecretStore.GetErrorMessage(errorCode);
                var msg = string.Format(CultureInfo.InvariantCulture, 
                    "The secret could not be written to the local default vault.  Error: {0}", errorMessage);
                ThrowTerminatingError(
                    new ErrorRecord(
                        new PSInvalidOperationException(msg),
                        "AddSecretCannotWrite",
                        ErrorCategory.WriteError,
                        this));
            }
        }

        #endregion
    }

    #endregion

    #region Remove-Secret

    /// <summary>
    /// Removes a secret by name from the local default vault.
    /// <summary>
    [Cmdlet(VerbsCommon.Remove, "Secret")]
    public sealed class RemoveSecretCommand : SecretsCmdlet
    {
        #region Parameters

        /// <summary>
        /// Gets or sets a name of the secret to be removed.
        /// </summary>
        [Parameter(Position=0, 
                   Mandatory=true,
                   ValueFromPipeline=true,
                   ValueFromPipelineByPropertyName=true)]
        [ValidateNotNullOrEmpty]
        public string Name { get; set; }

        /// <summary>
        /// Gets or sets an optional extension vault name.
        /// </summary>
        [Parameter(Position=1)]
        public string Vault { get; set; }

        #endregion

        #region Overrides

        protected override void ProcessRecord()
        {
            // Remove from specified vault.
            if (!string.IsNullOrEmpty(Vault))
            {
                var extensionModule = GetExtensionVault(Vault);
                extensionModule.InvokeRemoveSecret(
                    cmdlet: this,
                    name: Name);
                
                return;
            }

            // Remove from local built-in default vault.
            int errorCode = 0;
            if (!LocalSecretStore.DeleteObject(
                name: Name,
                ref errorCode))
            {
                var errorMessage = LocalSecretStore.GetErrorMessage(errorCode);
                var msg = string.Format(CultureInfo.InvariantCulture, 
                    "The secret could not be removed from the local default vault. Error: {0}", errorMessage);
                ThrowTerminatingError(
                    new ErrorRecord(
                        new PSInvalidOperationException(msg),
                        "RemoveSecretCannotDelete",
                        ErrorCategory.InvalidOperation,
                        this));
            }
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
                if (!LocalSecretStore.WriteObject(
                    Name,
                    Secret.BaseObject,
                    ref errorCode))
                {
                    var msg = LocalSecretStore.GetErrorMessage(errorCode);
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

        [Parameter]
        public SwitchParameter All { get; set; }

        protected override void EndProcessing()
        {
            int errorCode = 0;
            string msg = null;

            if (!string.IsNullOrEmpty(Name) && (Name.IndexOf('*') < 0))
            {
                if (LocalSecretStore.ReadObject(
                    Name,
                    out object outObject,
                    ref errorCode))
                {
                    WriteObject(outObject);
                    return;
                }

                msg = LocalSecretStore.GetErrorMessage(errorCode);
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
            if (LocalSecretStore.EnumerateObjects(
                filter,
                All,
                out KeyValuePair<string,object>[] outObjects,
                ref errorCode))
            {
                WriteObject(outObjects);
                return;
            }

            msg = LocalSecretStore.GetErrorMessage(errorCode);
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
            
            if (!LocalSecretStore.DeleteObject(
                Name,
                ref errorCode))
            {
                var msg = LocalSecretStore.GetErrorMessage(errorCode);
                WriteError(
                    new ErrorRecord(
                        new System.InvalidOperationException(
                            string.Format(CultureInfo.InvariantCulture, "Failed to remove secret {0} in vault, with error: {1}", Name, msg)),
                        "SecretRemoveError",
                        ErrorCategory.InvalidOperation,
                        this));
            }
        }
    }

    #endregion
}
