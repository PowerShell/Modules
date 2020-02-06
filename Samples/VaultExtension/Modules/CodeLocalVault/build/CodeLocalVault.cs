// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using Microsoft.PowerShell.SecretsManagement;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Security;

namespace CodeLocalVault
{
    #region ImplementingClass

    public class ImplementingClass : SecretsManagementExtension
    {
        #region Constructors

        private ImplementingClass() : base(string.Empty)
        { }

        public ImplementingClass(string vaultName) : base(vaultName)
        { }

        #endregion

        #region Abstract implementations

        public override bool SetSecret(
            string name,
            object secret,
            IReadOnlyDictionary<string, object> parameters,
            out Exception error)
        {
            var results = PowerShellInvoker.InvokeCommand(
                command: "Set-Secret",
                args: new object[] { name, secret },
                dataStreams: out PSDataStreams dataStreams);

            if (dataStreams.Error.Count > 0)
            {
                error = dataStreams.Error[0].Exception;
                return false;
            }

            error = null;
            return true;
        }

        public override object GetSecret(
            string name,
            IReadOnlyDictionary<string, object> parameters,
            out Exception error)
        {
            var result = PowerShellInvoker.InvokeCommand(
                command: "Get-Secret",
                args: new object[] { name },
                dataStreams: out PSDataStreams dataStreams);

            error = dataStreams.Error.Count > 0 ? dataStreams.Error[0].Exception : null;

            return result.Count > 0 ? result[0] : null;
        }

        public override bool RemoveSecret(
            string name,
            IReadOnlyDictionary<string, object> parameters,
            out Exception error)
        {
            PowerShellInvoker.InvokeCommand(
                command: "Remove-Secret",
                args: new object[] { name },
                dataStreams: out PSDataStreams dataStreams);

            if (dataStreams.Error.Count > 0)
            {
                error = dataStreams.Error[0].Exception;
                return false;
            }

            error = null;
            return true;
        }

        public override KeyValuePair<string, string>[] GetSecretInfo(
            string filter,
            IReadOnlyDictionary<string, object> parameters,
            out Exception error)
        {
            var results = PowerShellInvoker.InvokeCommand(
                command: "Enumerate-Secrets",
                args: new object[] { filter },
                dataStreams: out PSDataStreams dataStreams);

            error = dataStreams.Error.Count > 0 ? dataStreams.Error[0].Exception : null;

            var list = new List<KeyValuePair<string, string>>(results.Count);
            foreach (dynamic result in results)
            {
                string typeName;
                var resultValue = (result.Value is PSObject) ? ((PSObject)result.Value).BaseObject : result.Value;
                switch (resultValue)
                {
                    case byte[] blob:
                        typeName = nameof(SupportedTypes.ByteArray);
                        break;

                    case string str:
                        typeName = nameof(SupportedTypes.String);
                        break;

                    case SecureString sstr:
                        typeName = nameof(SupportedTypes.SecureString);
                        break;

                    case PSCredential cred:
                        typeName = nameof(SupportedTypes.PSCredential);
                        break;

                    case Hashtable ht:
                        typeName = nameof(SupportedTypes.Hashtable);
                        break;

                    default:
                        typeName = nameof(SupportedTypes.Unknown);
                        break;
                }

                list.Add(
                    new KeyValuePair<string, string>(
                        key: result.Name,
                        value: typeName));
            }

            return list.ToArray();
        }

        #endregion
    }

    #endregion

    #region PowerShellInvoker

    internal static class PowerShellInvoker
    {
        #region Members

        private const string FunctionsDefScript = @"
            function Get-Path
            {
                $path = Join-Path $env:TEMP 'TVECode'
                if (! (Test-Path -Path $path))
                {
                    [System.IO.Directory]::CreateDirectory($path)
                }

                return $path
            }

            function Get-Secret
            {
                param(
                    [Parameter(Mandatory=$true)]
                    [ValidateNotNullOrEmpty()]
                    [string] $Name
                )

                if ([WildcardPattern]::ContainsWildcardCharacters($Name))
                {
                    throw ""The Name parameter cannot contain any wild card characters.""
                }

                $filePath = Join-Path -Path (Get-Path) -ChildPath ""${Name}.xml""
    
                if (! (Test-Path -Path $filePath))
                {
                    return
                }

                Import-CliXml -Path $filePath
            }

            function Enumerate-Secrets
            {
                param(
                    [string] $Name
                )

                if ([string]::IsNullOrEmpty($Name)) { $Name = '*' }

                $files = dir(Join-Path -Path (Get-Path) -ChildPath ""${Name}.xml"") 2>$null

                foreach ($file in $files)
                {
                    $secretName = [System.IO.Path]::GetFileNameWithoutExtension((Split-Path $file -Leaf))
                    $secret = Import-Clixml -Path $file.FullName
                    Write-Output([pscustomobject] @{
                        Name = $secretName
                        Value = $secret
                    })
                }
            }

            function Set-Secret
            {
                param(
                    [Parameter(Mandatory=$true)]
                    [ValidateNotNullOrEmpty()]
                    [string] $Name,

                    [Parameter(Mandatory=$true)]
                    [ValidateNotNull()]
                    [object] $Secret
                )

                $filePath = Join-Path -Path (Get-Path) ""${Name}.xml""
                if (Test-Path -Path $filePath)
                {
                    Write-Error ""Secret name, $Name, is already used in this vault.""
                    return
                }

                $Secret | Export-Clixml -Path $filePath
            }

            function Remove-Secret
            {
                param(
                    [string] $Name
                )

                $filePath = Join-Path -Path (Get-Path) ""${Name}.xml""
                if (! (Test-Path -Path $filePath))
                {
                    Write-Error ""The secret $Name does not exist""
                    return
                }

                Remove-Item -Path $filePath
            }
        ";

        #endregion

        #region Methods

        public static Collection<PSObject> InvokeCommand(
            string command,
            object[] args,
            out PSDataStreams dataStreams)
        {
            using (var powerShell = System.Management.Automation.PowerShell.Create())
            {
                powerShell.AddScript(FunctionsDefScript).Invoke();
                powerShell.Commands.Clear();

                var results = powerShell.AddCommand(command).AddParameters(args).Invoke();
                dataStreams = powerShell.Streams;
                return results;
            }
        }
    }

    #endregion

    #endregion
}
