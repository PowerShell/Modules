// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Management.Automation;

namespace Microsoft.PowerShell.SecretsManagement
{
    #region Extension vault module class

    /// <summary>
    /// Class that contains all vault module information and secret manipulation methods.
    /// </summary>
    internal class ExtensionVaultModule
    {
        //
        // Default commands:
        //
        //  # Retrieve secret object from vault and write to output
        //  Get-Secret (required)
        //      [string] $Name
        //
        //  # Add secret object to vault.
        //  Set-Secret (optional)
        //      [string] $Name
        //      [PSObject] $secret
        //
        //  # Remove secret object from vault.
        //  Remove-Secret (optional)
        //      [string] $Name
        //

        #region Members

        #region Strings

        private const string DefaultSecretGetCmd = "Get-Secret";
        private const string DefaultSecretSetCmd = "Set-Secret";
        private const string DefaultSecretRemoveCmd = "Remove-Secret";

        private const string ModuleNameStr = "ModuleName";
        private const string ModulePathStr = "ModulePath";
        private const string SecretGetScriptStr = "SecretGetScript";
        private const string SecretGetParamsStr = "SecretGetParams";
        private const string SecretSetScriptStr = "SecretSetScript";
        private const string SecretSetParamsStr = "SecretSetParams";
        private const string SecretRemoveScriptStr = "SecretRemoveScript";
        private const string SecretRemoveParamsStr = "SecretRemoveParams";
        private const string HaveDefaultGetStr = "HaveDefaultGet";
        private const string HaveDefaultSetStr = "HaveDefaultSet";
        private const string HaveDefaultRemoveStr = "HaveDefaultRemove";

        private const string RunCommandScript = @"
            param(
                [string] $ModulePath,
                [string] $ModuleName,
                [string] $CommandName,
                [hashtable] $Params
            )

            Import-Module -Name $ModuleName

            & ""$ModuleName\$CommandName"" @Params
        ";

        private const string RunScriptScript = @"
            param (
                [ScriptBlock] $sb,
                [hashtable] $Params
            )

            if ($Params -ne $null)
            {
                & $sb @Params
            }
            else
            {
                & $sb
            }
        ";

        #endregion

        private readonly bool _haveDefaultGetCommand;
        private readonly bool _haveDefaultSetCommand;
        private readonly bool _haveDefaultRemoveCommand;
        private ScriptBlock _GetSecretScriptBlock;
        private ScriptBlock _SetSecretScriptBlock;
        private ScriptBlock _RemoveSecretScriptBlock;

        #endregion

        #region Properties

        /// <summary>
        /// Module name to qualify module commands.
        /// </summary>
        public string ModuleName { get; }

        /// <summary>
        /// Module path.
        /// </summary>
        public string ModulePath { get; }

        /// <summary>
        /// Optional script to get secret from vault.
        /// <summary>
        public string SecretGetScript { get; }

        /// <summary>
        /// Optional local store name for get secret script parameters.
        /// <summary>
        public string SecretGetParamsName { get; }

        /// <summary>
        /// Optional script to add secret to vault.
        /// </summary>
        public string SecretSetScript { get; }

        /// <summary>
        /// Optional local store name for set secret script parameters.
        /// <summary>
        public string SecretSetParamsName { get; }

        /// <summary>
        /// Optional script to remove secret from vault.
        /// </summary>
        public string SecretRemoveScript { get; }

        /// <summary>
        /// Optional local store name for remove secret script parameters.
        /// </summary>
        public string SecretRemoveParamsName { get; }

        #endregion

        #region Constructor

        private ExtensionVaultModule() { }

        /// <summary>
        /// Initializes a new instance of ExtensionVaultModule
        /// </summary>
        public ExtensionVaultModule(Hashtable vaultInfo)
        {
            // Required module information.
            ModuleName = (string) vaultInfo[ModuleNameStr];
            ModulePath = (string) vaultInfo[ModulePathStr];
            _haveDefaultGetCommand = (bool) vaultInfo[HaveDefaultGetStr];
            _haveDefaultSetCommand = (bool) vaultInfo[HaveDefaultSetStr];
            _haveDefaultRemoveCommand = (bool) vaultInfo[HaveDefaultRemoveStr];

            // Optional Get-Secret script block.
            SecretGetScript = (vaultInfo.ContainsKey(SecretGetScriptStr)) ?
                (string) vaultInfo[SecretGetScriptStr] : string.Empty;
            SecretGetParamsName = (vaultInfo.ContainsKey(SecretGetParamsStr)) ?
                (string) (string) vaultInfo[SecretGetParamsStr] : string.Empty;

            // Optional Set-Secret script block.
            SecretSetScript = (vaultInfo.ContainsKey(SecretSetScriptStr)) ?
                (string) vaultInfo[SecretSetScriptStr] : string.Empty;
            SecretSetParamsName = (vaultInfo.ContainsKey(SecretSetParamsStr)) ?
                (string) vaultInfo[SecretSetParamsStr] : string.Empty;

            // Optional Remove-Secret script block.
            SecretRemoveScript = (vaultInfo.ContainsKey(SecretRemoveScriptStr)) ?
                (string) vaultInfo[SecretRemoveScriptStr] : string.Empty;
            SecretRemoveParamsName = (vaultInfo.ContainsKey(SecretRemoveParamsStr)) ?
                (string) vaultInfo[SecretRemoveParamsStr] : string.Empty;
        }

        #endregion

        #region Public methods

        /// <summary>
        /// Invokes module command to get secret from this vault.
        /// Output is sent to cmdlet pipe.
        /// <summary>
        /// <param name="cmdlet">PowerShell cmdlet to invoke module command on.</param>
        /// <param name="name">Name of secret to get</param>
        public void InvokeGetSecret(
            PSCmdlet cmdlet,
            string name)
        {
            Hashtable parameters = null;

            if (_haveDefaultGetCommand)
            {
                parameters = new Hashtable() {
                    { "Name", name }
                };

                cmdlet.InvokeCommand.InvokeScript(
                    RunCommandScript,
                    new object[] { ModulePath, ModuleName, DefaultSecretGetCmd, parameters });

                return;
            }

            // Get stored script parameters if provided.
            parameters = GetParamsFromStore(SecretGetParamsName);

            // Use provided secret get script.
            if (_GetSecretScriptBlock == null)
            {
                // TODO: !! Add support for creation of *untrusted* script block. !!
                _GetSecretScriptBlock = ScriptBlock.Create(SecretGetScript);
            }

            cmdlet.InvokeCommand.InvokeScript(
                RunScriptScript,
                new object[] { _GetSecretScriptBlock, parameters });
        }

        /// <summary>
        /// Invokes module command to add a secret to this vault.
        /// </summary>
        /// <param name="cmdlet">PowerShell cmdlet to invoke module command on.</param>
        /// <param name="name">Name of secret to add.</param>
        /// <param name="secret">Secret object to ad to vault.</param>
        public void InvokeSetSecret(
            PSCmdlet cmdlet,
            string name,
            PSObject secret)
        {
            Hashtable parameters = null;

            if (_haveDefaultSetCommand)
            {
                parameters = new Hashtable() {
                    { "Name", name },
                    { "Secret", secret }
                };

                cmdlet.InvokeCommand.InvokeScript(
                    RunCommandScript,
                    new object[] { ModulePath, ModuleName, DefaultSecretSetCmd, parameters });

                return;
            }

            // Get stored script parameters if provided.
            parameters = GetParamsFromStore(SecretSetParamsName);

            // Use provided secret get script.
            if (_SetSecretScriptBlock == null)
            {
                // TODO: !! Add support for creation of *untrusted* script block. !!
                _SetSecretScriptBlock = ScriptBlock.Create(SecretSetScript);
            }

            cmdlet.InvokeCommand.InvokeScript(
                RunScriptScript,
                new object[] { _SetSecretScriptBlock, parameters });
        }

        /// <summary>
        /// Invokes module command to remove a secret from this vault.
        /// </summary>
        /// <param name="cmdlet">PowerShell cmdlet to invoke module command on.</param>
        /// <param name="name">Name of secret to remove.</param>
        public void InvokeRemoveSecret(
            PSCmdlet cmdlet,
            string name)
        {
            Hashtable parameters = null;

            if (_haveDefaultRemoveCommand)
            {
                parameters = new Hashtable() {
                    { "Name", name }
                };

                cmdlet.InvokeCommand.InvokeScript(
                    RunCommandScript,
                    new object[] { ModulePath, ModuleName, DefaultSecretRemoveCmd, parameters });

                return;
            }

            // Get stored script parameters if provided.
            parameters = GetParamsFromStore(SecretRemoveParamsName);

            // Use provided secret get script.
            if (_RemoveSecretScriptBlock == null)
            {
                // TODO: !! Add support for creation of *untrusted* script block. !!
                _RemoveSecretScriptBlock = ScriptBlock.Create(SecretSetScript);
            }

            cmdlet.InvokeCommand.InvokeScript(
                RunScriptScript,
                new object[] { _RemoveSecretScriptBlock, parameters });
        }

        #endregion

        #region Private methods

        private static Hashtable GetParamsFromStore(string paramsName)
        {
            Hashtable parameters = null;
            if (!string.IsNullOrEmpty(paramsName))
            {
                // TODO: Refactor default store to provider agnostic API.
                int errorCode = 0;
                if (CredMan.ReadObject(
                    paramsName,
                    out object outObject,
                    ref errorCode))
                {
                    parameters = outObject as Hashtable;
                }
            }

            return parameters;
        }

        #endregion
    }

    #endregion

    #region Register-SecretsVault

    /// <summary>
    /// Cmdlet to register a remote secrets vaults provider module
    /// </summary>
    [Cmdlet(VerbsLifecycle.Register, "SecretsVault")]
    public sealed class RegisterSecretsVaultCommand : PSCmdlet
    {
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
        public ScriptBlock SecretGetScript { get; set; }

        /// <summary>
        /// Gets or sets an optional Hashtable of parameters to the registered vault extension module 
        /// script used to retrieve secrets from the vault.
        /// Note: This may contain sensitive information, and so should be stored in local vault.
        /// </summary>
        [Parameter]
        public Hashtable SecretGetParameters { get; set; }

        /// <summary>
        /// Gets or sets an optional ScriptBlock used to add a secret to the registered vault.
        /// Note: Add script block text only and convert to ScriptBlock (safely) as needed.
        /// </summary>
        [Parameter]
        [ValidateNotNull]
        public ScriptBlock SecretSetScript { get; set; }

        /// <summary>
        /// Gets or sets an optional Hashtable of parameters to the registered vault extension module
        /// script used to add secrets to the vault.
        /// </summary>
        [Parameter]
        public Hashtable SecretSetParameters { get; set; }

        /// <summary>
        /// Gets or sets an optional ScriptBlock used to remove a secret from the registered vault.
        /// </summary>
        [Parameter]
        [ValidateNotNull]
        public ScriptBlock SecretRemoveScript { get; set; }

        /// <summary>
        /// Gets or sets an optional Hashtable of parameters to the registered vault extension module
        /// script used to remove secrets from the vault.
        /// </summary>
        [Parameter]
        public Hashtable SecretRemoveParameters { get; set; }

        #endregion

        #region Members

        private const string ConvertJsonToHashtableScript = @"
            param (
                [string] $json
            )

            function ConvertToHash
            {
                param (
                    [pscustomobject] $object
                )

                $output = @{}
                $object | Get-Member -MemberType NoteProperty | ForEach-Object {
                    $name = $_.Name
                    $value = $object.($name)

                    if ($value -is [object[]])
                    {
                        $array = @()
                        $value | ForEach-Object {
                            $array += (ConvertToHash $_)
                        }
                        $output.($name) = $array
                    }
                    elseif ($value -is [pscustomobject])
                    {
                        $output.($name) = (ConvertToHash $value)
                    }
                    else
                    {
                        $output.($name) = $value
                    }
                }

                $output
            }

            $customObject = ConvertFrom-Json $json
            return ConvertToHash $customObject
        ";

        #endregion

        #region Properties

        private static string SecretsRegistryFilePath
        {
            get
            {
                return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + 
                    @"\Microsoft\Windows\PowerShell\SecretVaultRegistry";
            }
        }

        #endregion

        #region Helper Methods

        private Hashtable ConvertJsonToHashtable(string json)
        {
            var psObject = this.InvokeCommand.InvokeScript(
                script: ConvertJsonToHashtableScript,
                args: new object[] { json });

            return psObject[0].BaseObject as Hashtable;
        }

        /// <summary>
        /// Reads the current user secret vault registry information from file.
        /// </summary>
        /// <returns>Hashtable containing registered vault information.</returns>
        private Hashtable ReadSecretVaultRegistry()
        {
            if (!File.Exists(SecretsRegistryFilePath))
            {
                return null;
            }

            string jsonInfo = File.ReadAllText(SecretsRegistryFilePath);
            return ConvertJsonToHashtable(jsonInfo);
        }

        /// <summary>
        /// Writes the Hashtable registered vault information data to file as json.
        /// </summary>
        /// <param>Hashtable containing registered vault information.</param>
        private void WriteSecretVaultRegistry(Hashtable dataToWrite)
        {
            var psObject = this.InvokeCommand.InvokeScript(@"param ([hashtable] $dataToWrite) ConvertTo-Json $dataToWrite", new object[] { dataToWrite });
            string jsonInfo = psObject[0].BaseObject as string;
            File.WriteAllText(SecretsRegistryFilePath, jsonInfo);
        }

        #endregion

        #region Overrides

        protected override void EndProcessing()
        {
            // Load module (Get-Module -List ?, but no parameters)
            // Get module (full) name
            // Get module exported commands
            // Verify commands (or equivalent script blocks):
            //  Add-Secret
            //      [-Name] <string>
            //      [-Secret] <psobject>
            //  Get-Secret
            //      [-Name] <string>
            //  Remove-Secret
            //      [-Name] <string>
            //
            // Run 'Should Process', or skip if -Force
            //  Read json database file (convert to hashtable)
            //  Replace single default entry with new
            //  Write file

            // TODO:  !!Test only!!
            var testInfoJson = @"
            {
                ""RemoteVault2"": {
                    ""ModuleName"": ""Module.RemoteVault2"",
                    ""ModulePath"": ""c:\\temp\\Modules\\Module.RemoteVault2\\Module.RemoteVault2.psd1"",
                    ""HaveDefaultGet"": true,
                    ""HaveDefaultSet"": false,
                    ""HaveDefaultRemove"": false
                },
                ""LocalVault1"": {
                    ""ModuleName"": ""Module.LocalVault1"",
                    ""ModulePath"": ""c:\\temp\\Modules\\Module.LocalVault1\\Module.LocalVault1.psd1"",
                    ""SecretGetScript"": ""Module.LocalVault1\\Get-Secret @Params"",
                    ""SecretGetParams"": {
                        ""Verification"": ""SecretValue"",
                        ""AccountName"": ""SomeAccount""
                    },
                    ""SecretSetScript"": ""Module.LocalVault1\\Set-Secret @Params"",
                    ""SecretSetParams"": {
                        ""Verification"": ""SecretValue"",
                        ""AccountName"": ""SomeAccount""
                    },
                    ""SecretRemoveScript"": ""Module.LocalVault1\\Remove-Secret @Params"",
                    ""SecretRemoveParams"": {
                        ""Verification"": ""SecretValue"",
                        ""AccountName"": ""SomeAccount""
                    },
                    ""HaveDefaultGet"": false,
                    ""HaveDefaultSet"": false,
                    ""HaveDefaultRemove"": false
                },
                ""RemoteVault1"": {
                    ""ModuleName"": ""Module.RemoteVault1"",
                    ""ModulePath"": ""c:\\temp\\Modules\\Module.RemoteVault1\\Module.RemoteVault1.psd1"",
                    ""SecretGetScript"": ""Module.RemoteVault1\\Get-Secret @Params"",
                    ""SecretGetParams"": {
                        ""Verification"": ""SecretValue"",
                        ""AccountName"": ""SomeAccount""
                    },
                    ""HaveDefaultGet"": false,
                    ""HaveDefaultSet"": false,
                    ""HaveDefaultRemove"": false
                },
                ""LocalVault2"": {
                    ""ModuleName"": ""Module.LocalVault2"",
                    ""ModulePath"": ""c:\\temp\\Modules\\Module.LocalVault2\\Module.LocalVault2.psd1"",
                    ""SecretGetScript"": ""Module.LocalVault2\\Get-Secret @Params"",
                    ""SecretGetParams"": {
                        ""Verification"": ""SecretValue"",
                        ""AccountName"": ""SomeAccount""
                    },
                    ""SecretSetScript"": ""Module.LocalVault2\\Set-Secret @Params"",
                    ""SecretSetParams"": {
                        ""Verification"": ""SecretValue"",
                        ""AccountName"": ""SomeAccount""
                    },
                    ""HaveDefaultGet"": false,
                    ""HaveDefaultSet"": false,
                    ""HaveDefaultRemove"": false
                }
            }
            ";

            var hashtable = ConvertJsonToHashtable(testInfoJson);
            WriteObject(hashtable);

            WriteSecretVaultRegistry(hashtable);
            var readHashtable = ReadSecretVaultRegistry();
            WriteObject(readHashtable);
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
