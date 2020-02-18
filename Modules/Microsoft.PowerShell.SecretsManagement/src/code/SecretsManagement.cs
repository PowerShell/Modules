// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Management.Automation;
using System.Security;

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
        /// Optional name of secret parameters used by vault module.
        /// </summary>
        public string VaultParametersName { get; }

        /// <summary>
        /// Name of assembly implementing the SecretsManagement type.
        /// </summary>
        public string ImplementingTypeAssemblyName { get; }

        /// <summary>
        /// Name of type that implements the SecretsManagement type.
        /// </summary>
        public string ImplementingTypeName { get; }

        #endregion

        #region Constructor

        internal SecretsVaultInfo(
            string name,
            ExtensionVaultModule vaultInfo)
        {
            Name = name;
            ModuleName = vaultInfo.ModuleName;
            ModulePath = vaultInfo.ModulePath;
            VaultParametersName = vaultInfo.VaultParametersName;
            ImplementingTypeAssemblyName = vaultInfo.ImplementingTypeAssemblyName;
            ImplementingTypeName = vaultInfo.ImplementingTypeName;
        }

        internal SecretsVaultInfo(
            string defaultVaultName)
        {
            Name = defaultVaultName;
            ModuleName = string.Empty;
            ModulePath = string.Empty;
            ImplementingTypeAssemblyName = string.Empty;
            ImplementingTypeName = string.Empty;
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

        internal const string ScriptParamTag = "_SPT_Parameters_";
        internal const string BuiltInLocalVault = "BuiltInLocalVault";
        internal const string ImplementingModule = "SecretsManagementExtension";

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
        /// Gets or sets the module name or file path of the vault extension module to register.
        /// </summary>
        [Parameter(Position=1, Mandatory=true)]
        [ValidateNotNullOrEmpty]
        public string ModuleName { get; set; }

        /// <summary>
        /// Gets or sets an optional Hashtable of parameters by name/value pairs.
        /// The hashtable is stored securely in the local store, and is made available to the 
        /// SecretsManagementExtension implementing type or module script functions.
        /// </summary>
        [Parameter]
        public Hashtable VaultParameters { get; set; }

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
                        "RegisterSecretsVaultInvalidVaultName",
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

            if (!ShouldProcess(Name, VerbsLifecycle.Register))
            {
                return;
            }

            var moduleInfo = GetModuleInfo(ModuleName);
            if (moduleInfo == null)
            {
                var msg = string.Format(CultureInfo.InvariantCulture, 
                    "Could not load and retrieve module information for module: {0}.",
                    ModuleName);

                ThrowTerminatingError(
                    new ErrorRecord(
                        new PSInvalidOperationException(msg),
                        "RegisterSecretsVaultCantGetModuleInfo",
                        ErrorCategory.InvalidOperation,
                        this));
            }

            var modulePath = moduleInfo.Path;
            var dirPath = System.IO.File.Exists(modulePath) ? System.IO.Path.GetDirectoryName(modulePath) : modulePath;

            // Check module required modules for implementing type of SecretsManagementExtension class.
            Type implementingType = GetImplementingTypeFromRequiredAssemblies(moduleInfo);

            // Check if module supports implementing functions.
            var haveScriptFunctionImplementation = CheckForImplementingModule(
                dirPath: dirPath,
                error: out Exception error);

            if (implementingType == null && !haveScriptFunctionImplementation)
            {
                var invalidException = new PSInvalidOperationException(
                    message: "Could not find a SecretsManagementExtension implementing type, or a valid implementing script module.",
                    innerException: error);

                ThrowTerminatingError(
                    new ErrorRecord(
                        invalidException,
                        "RegisterSecretsVaultCantFindImplementingTypeOrScriptModule",
                        ErrorCategory.ObjectNotFound,
                        this));
            }

            vaultInfo.Add(ExtensionVaultModule.ModulePathStr, dirPath);
            vaultInfo.Add(ExtensionVaultModule.ModuleNameStr, moduleInfo.Name);

            vaultInfo.Add(
                key: ExtensionVaultModule.ImplementingTypeStr, 
                value: new Hashtable() {
                    { "AssemblyName", implementingType != null ? implementingType.Assembly.GetName().Name : string.Empty },
                    { "TypeName", implementingType != null ? implementingType.FullName: string.Empty }
                });

            vaultInfo.Add(
                key: ExtensionVaultModule.ImplementingFunctionsStr,
                value: haveScriptFunctionImplementation);

            // Store the optional secret parameters
            StoreVaultParameters(
                vaultInfo: vaultInfo,
                vaultName: Name,
                parameters: VaultParameters);

            // Register new secret vault information.
            RegisteredVaultCache.Add(
                keyName: Name,
                vaultInfo: vaultInfo);
        }

        #endregion

        #region Private methods

        private static Type GetImplementingTypeFromRequiredAssemblies(
            PSModuleInfo moduleInfo)
        {
            var extensionType = typeof(Microsoft.PowerShell.SecretsManagement.SecretsManagementExtension);
            foreach (var requiredAssembly in moduleInfo.RequiredAssemblies)
            {
                var assemblyName = System.IO.Path.GetFileNameWithoutExtension(requiredAssembly);
                foreach (var assembly in System.AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (assembly.GetName().Name.Equals(assemblyName, StringComparison.OrdinalIgnoreCase))
                    {
                        foreach (var assemblyType in assembly.GetTypes())
                        {
                            if (extensionType.IsAssignableFrom(assemblyType))
                            {
                                return assemblyType;
                            }
                        }
                    }
                }
            }

            return null;
        }

        private static bool CheckForImplementingModule(
            string dirPath,
            out Exception error)
        {
            // An implementing module will be in a subfolder with module name 'SecretsManagementExtension',
            // and will export the four required functions: Set-Secret, Get-Secret, Remove-Secret, Get-SecretInfo.
            var implementingModulePath = System.IO.Path.Combine(dirPath, ImplementingModule);
            var moduleInfo = GetModuleInfo(implementingModulePath);
            if (moduleInfo == null)
            {
                error = new ItemNotFoundException("Implementing script module not found.");
                return false;
            }

            // Get-Secret function
            if (!moduleInfo.ExportedFunctions.ContainsKey("Get-Secret"))
            {
                error = new ItemNotFoundException("Get-Secret function not found.");
                return false;
            }
            var funcInfo = moduleInfo.ExportedFunctions["Get-Secret"];
            if (!funcInfo.Parameters.ContainsKey("Name"))
            {
                error = new ItemNotFoundException("Get-Secret Name parameter not found.");
                return false;
            }
            if (!funcInfo.Parameters.ContainsKey("AdditionalParameters"))
            {
                error = new ItemNotFoundException("Get-Secret AdditionalParameters parameter not found.");
                return false;
            }

            // Set-Secret function
            if (!moduleInfo.ExportedFunctions.ContainsKey("Set-Secret"))
            {
                error = new ItemNotFoundException("Set-Secret function not found.");
                return false;
            }
            funcInfo = moduleInfo.ExportedFunctions["Set-Secret"];
            if (!funcInfo.Parameters.ContainsKey("Name"))
            {
                error = new ItemNotFoundException("Set-Secret Name parameter not found.");
                return false;
            }
            if (!funcInfo.Parameters.ContainsKey("Secret"))
            {
                error = new ItemNotFoundException("Set-Secret Secret parameter not found.");
                return false;
            }
            if (!funcInfo.Parameters.ContainsKey("AdditionalParameters"))
            {
                error = new ItemNotFoundException("Set-Secret AdditionalParameters parameter not found.");
                return false;
            }

            // Remove-Secret function
            if (!moduleInfo.ExportedFunctions.ContainsKey("Remove-Secret"))
            {
                error = new ItemNotFoundException("Remove-Secret function not found.");
                return false;
            }
            funcInfo = moduleInfo.ExportedFunctions["Remove-Secret"];
            if (!funcInfo.Parameters.ContainsKey("Name"))
            {
                error = new ItemNotFoundException("Remove-Secret Name parameter not found.");
                return false;
            }
            if (!funcInfo.Parameters.ContainsKey("AdditionalParameters"))
            {
                error = new ItemNotFoundException("Remove-Secret AdditionalParameters parameter not found.");
                return false;
            }

            // Get-SecretInfo function
            if (!moduleInfo.ExportedFunctions.ContainsKey("Get-SecretInfo"))
            {
                error = new ItemNotFoundException("Get-SecretInfo function not found.");
                return false;
            }
            funcInfo = moduleInfo.ExportedFunctions["Get-SecretInfo"];
            if (!funcInfo.Parameters.ContainsKey("Filter"))
            {
                error = new ItemNotFoundException("Get-SecretInfo Filter parameter not found.");
                return false;
            }
            if (!funcInfo.Parameters.ContainsKey("AdditionalParameters"))
            {
                error = new ItemNotFoundException("Get-SecretInfo AdditionalParameters parameter not found.");
                return false;
            }

            error = null;
            return true;
        }

        private static PSModuleInfo GetModuleInfo(
            string modulePath)
        {
            // Get module information by loading it.
            var results = PowerShellInvoker.InvokeScript(
                script: @"
                    param ([string] $ModulePath)

                    Import-Module -Name $ModulePath -Force -PassThru
                ",
                args: new object[] { modulePath },
                out Exception _);
            
            return (results.Count == 1) ? results[0].BaseObject as PSModuleInfo : null;
        }

        private void StoreVaultParameters(
            Hashtable vaultInfo,
            string vaultName,
            Hashtable parameters)
        {
            var parametersName = string.Empty;

            if (parameters != null)
            {
                // Generate unique name for parameters based on vault name.
                //  e.g., "_SPT_Parameters_VaultName_"
                parametersName = ScriptParamTag + vaultName + "_";

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
                key: ExtensionVaultModule.VaultParametersStr,
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

            if (vaultName.Equals(RegisterSecretsVaultCommand.BuiltInLocalVault, StringComparison.OrdinalIgnoreCase))
            {
                var msg = string.Format(CultureInfo.InvariantCulture, 
                    "The {0} vault cannot be removed.", 
                    RegisterSecretsVaultCommand.BuiltInLocalVault);

                WriteError(
                    new ErrorRecord(
                        new PSArgumentException(msg),
                        "RegisterSecretsVaultInvalidVaultName",
                        ErrorCategory.InvalidArgument,
                        this));

                return;
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
            RemoveParamSecrets(removedVaultInfo, ExtensionVaultModule.VaultParametersStr);
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

    #region Set-VaultParameters

    // TODO: Implement.

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
            // Look up extension module.
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

            // Always list the 'BuiltInLocalVault' first
            if (namePattern.IsMatch(RegisterSecretsVaultCommand.BuiltInLocalVault))
            {
                WriteObject(
                    new SecretsVaultInfo(RegisterSecretsVaultCommand.BuiltInLocalVault));
            }

            // Then list all extension vaults in sorted order.
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

    #region Get-SecretInfo

    /// <summary>
    /// Enumerates secrets by name, wild cards are allowed.
    /// If no name is provided then all secrets are returned.
    /// If no vault is specified then all vaults are searched.
    /// </summary>
    [Cmdlet(VerbsCommon.Get, "SecretInfo")]
    [OutputType(typeof(PSObject))]
    public sealed class GetSecretInfoCommand : SecretsCmdlet
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
                WriteResults(
                    extensionModule.VaultName,
                    extensionModule.InvokeGetSecretInfo(
                        filter: Name,
                        cmdlet: this));
                
                return;
            }

            // Search the local built in store first.
            SearchLocalStore(Name);

            // Then search through all extension vaults.
            foreach (var extensionModule in RegisteredVaultCache.VaultExtensions.Values)
            {
                try
                {
                    WriteResults(
                        extensionModule.VaultName,
                        extensionModule.InvokeGetSecretInfo(
                            filter: Name,
                            cmdlet: this));
                }
                catch (Exception ex)
                {
                    WriteError(
                        new ErrorRecord(
                            ex,
                            "GetSecretInfoException",
                            ErrorCategory.InvalidOperation,
                            this));
                }
            }
        }

        #endregion

        #region Private methods

        private void WriteResults(
            string vaultName,
            KeyValuePair<string, string>[] results,
            bool filterSpecialLocalNames = false)
        {
            // Ensure each vaults results are sorted by secret name.
            var sortedList = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in results)
            {
                if (filterSpecialLocalNames &&
                    item.Key.StartsWith(RegisterSecretsVaultCommand.ScriptParamTag))
                {
                    continue;
                }

                sortedList.Add(
                    key: item.Key,
                    value: item.Value);
            }

            foreach (var item in sortedList)
            {
                WritePSObject(
                    name: item.Key,
                    typeName: item.Value,
                    vaultName: vaultName);
            }
        }

        private void WritePSObject(
            string name,
            string typeName,
            string vaultName)
        {
            var psObject = new PSObject();
            psObject.Members.Add(
                new PSNoteProperty("Name", name));
            psObject.Members.Add(
                new PSNoteProperty("Vault", vaultName));
            psObject.Members.Add(
                new PSNoteProperty("TypeName", typeName));

            WriteObject(psObject);
        }

        private void SearchLocalStore(string name)
        {
            // Search through the built-in local vault.
            int errorCode = 0;
            if (LocalSecretStore.EnumerateObjectInfo(
                filter: Name,
                outObjectInfos: out KeyValuePair<string, string>[] outObjectInfos,
                errorCode: ref errorCode))
            {
                WriteResults(
                    vaultName: RegisterSecretsVaultCommand.BuiltInLocalVault,
                    results: outObjectInfos,
                    filterSpecialLocalNames: true);
            }
        }

        #endregion
    }

    #endregion

    #region Get-Secret

    /// <summary>
    /// Retrieves a secret by name, wild cards are not allowed.
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

        /// <summary>
        /// Gets or sets a switch that forces a string secret type to be returned as plain text.
        /// Otherwise the string is returned as a SecureString type.
        /// </summary>
        [Parameter(Position=2)]
        public SwitchParameter AsPlainText { get; set; }

        #endregion

        #region Overrides

        protected override void EndProcessing()
        {
            // Wild card characters are not supported in this cmdlet.
            if (WildcardPattern.ContainsWildcardCharacters(Name))
            {
                ThrowTerminatingError(
                    new ErrorRecord(
                        new ArgumentException("Name parameter cannot contain wildcard characters."),
                        "GetSecretNoWildcardCharsAllowed",
                        ErrorCategory.InvalidArgument,
                        this));
            }

            // Search single vault.
            if (!string.IsNullOrEmpty(Vault))
            {
                if (Vault.Equals(RegisterSecretsVaultCommand.BuiltInLocalVault, StringComparison.OrdinalIgnoreCase))
                {
                    if (!SearchLocalStore(Name))
                    {
                        WriteNotFoundError();
                    }
                    return;
                }

                var extensionModule = GetExtensionVault(Vault);
                var result = extensionModule.InvokeGetSecret(
                    name: Name,
                    cmdlet: this);

                if (result != null)
                {
                    WriteSecret(result);
                }
                else
                {
                    WriteNotFoundError();
                }

                return;
            }

            // First search the built-in local vault.
            if (SearchLocalStore(Name))
            {
                return;
            }

            // Then search through all extension vaults.
            foreach (var extensionModule in RegisteredVaultCache.VaultExtensions.Values)
            {
                try
                {
                    var result = extensionModule.InvokeGetSecret(
                        name: Name,
                        cmdlet: this);
                        
                    if (result != null)
                    {
                        WriteSecret(result);
                        return;
                    }
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

            WriteNotFoundError();
        }

        #endregion

        #region Private methods

        private void WriteSecret(object secret)
        {
            if (secret is PSObject secretPSObject)
            {
                secret = secretPSObject.BaseObject;
            }

            if (!AsPlainText && secret is string stringSecret)
            {
                // Write a string secret type only if explicitly requested with the -AsPlainText
                // parameter switch.  Otherwise return it as a SecureString type.
                WriteObject(ConvertToSecureString(stringSecret));
                return;
            }

            if (AsPlainText && secret is SecureString secureString)
            {
                // Convert secure string to plain text.
                var networkCred = new System.Net.NetworkCredential("", secureString);
                WriteObject(networkCred.Password);
                return;
            }

            WriteObject(secret);
        }

        private SecureString ConvertToSecureString(string secret)
        {
            var results = InvokeCommand.InvokeScript(
                script: @"
                    param ([string] $secret)

                    ConvertTo-SecureString -String $secret -AsPlainText -Force
                ",
                useNewScope: false,
                writeToPipeline: System.Management.Automation.Runspaces.PipelineResultTypes.None,
                input: null,
                args: new object[] { secret });
            
            return (results.Count == 1) ? results[0].BaseObject as SecureString : null;
        }

        private void WriteNotFoundError()
        {
            var msg = string.Format(CultureInfo.InvariantCulture, "The secret {0} was not found.", Name);
            WriteError(
                new ErrorRecord(
                    new ItemNotFoundException(msg),
                    "GetSecretNotFound",
                    ErrorCategory.ObjectNotFound,
                    this));
        }

        private bool SearchLocalStore(string name)
        {
            int errorCode = 0;
            if (LocalSecretStore.ReadObject(
                name: name,
                outObject: out object outObject,
                ref errorCode))
            {
                WriteSecret(outObject);
                return true;
            }

            return false;
        }

        #endregion
    }

    #endregion

    #region Set-Secret

    /// <summary>
    /// Adds a provided secret to the specified extension vault, 
    /// or the built-in default store if an extension vault is not specified.
    /// </summary>
    [Cmdlet(VerbsCommon.Set, "Secret", 
            DefaultParameterSetName = SecureStringParameterSet)]
    public sealed class SetSecretCommand : SecretsCmdlet
    {
        #region Members

        private const string SecureStringParameterSet = "SecureStringParameterSet";
        private const string ObjectParameterSet = "ObjectParameterSet";

        #endregion

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
        [Parameter(Position=1, Mandatory=true, ValueFromPipeline=true,
                   ParameterSetName = ObjectParameterSet)]
        public object Secret { get; set; }

        /// <summary>
        /// Gets or sets a SecureString value to be added to a vault.
        /// </summary>
        [Parameter(Position=1, Mandatory=true, ValueFromPipeline=true,
                   ParameterSetName = SecureStringParameterSet)]
        public SecureString SecureStringSecret { get; set; }

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
            if (ParameterSetName == SecureStringParameterSet)
            {
                Secret = SecureStringSecret;
            }

            var secretToWrite = (Secret is PSObject psObject) ? psObject.BaseObject : Secret;

            // Add to specified vault.
            if (!string.IsNullOrEmpty(Vault) && 
                !Vault.Equals(RegisterSecretsVaultCommand.BuiltInLocalVault, StringComparison.OrdinalIgnoreCase))
            {
                var extensionModule = GetExtensionVault(Vault);
                
                // If NoClobber is selected, then check to see if it already exists.
                if (NoClobber)
                {
                    var result = extensionModule.InvokeGetSecret(
                        name: Name,
                        cmdlet: this);

                    if (result != null)
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
                    name: Name,
                    secret: secretToWrite,
                    cmdlet: this);
                
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
                objectToWrite: secretToWrite,
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
            else
            {
                WriteVerbose(
                    string.Format("Secret {0} was successfully added to vault {1}.", Name, RegisterSecretsVaultCommand.BuiltInLocalVault));
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
        [Parameter(Position=1, Mandatory=true)]
        [ValidateNotNullOrEmpty]
        public string Vault { get; set; }

        #endregion

        #region Overrides

        protected override void ProcessRecord()
        {
            if (Vault.Equals(RegisterSecretsVaultCommand.BuiltInLocalVault, StringComparison.OrdinalIgnoreCase))
            {
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
                else
                {
                    WriteVerbose(
                        string.Format("Secret {0} was successfully removed from vault {1}.", Name, RegisterSecretsVaultCommand.BuiltInLocalVault));
                }

                return;
            }

            // Remove from extension vault.
            var extensionModule = GetExtensionVault(Vault);
            extensionModule.InvokeRemoveSecret(
                name: Name,
                cmdlet: this);
        }

        #endregion
    }

    #endregion
}
