using Microsoft.PowerShell.SecretsManagement;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Security;

namespace TestVault1Extension
{

    #region TestVault1Extension

    public class TestVault1Extension : SecretsManagementExtension
    {
        #region Constructors

        private TestVault1Extension() : base(string.Empty)
        { }

        public TestVault1Extension(string vaultName) : base(vaultName)
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

        // Ensure there is one instance of PowerShell per thread by using [ThreadStatic]
        // attribute to store each local thread instance.
        [ThreadStatic]
        private static System.Management.Automation.PowerShell _powerShell;

        private const string FunctionsDefScript = @"
            function Get-Path
            {
                $path = Join-Path $env:TEMP 'TestVault5'
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

        #region Constructor

        static PowerShellInvoker()
        {
            InitPowerShell();
        }

        private static void InitPowerShell()
        {
            _powerShell = System.Management.Automation.PowerShell.Create();
            _powerShell.AddScript(FunctionsDefScript).Invoke();
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

                InitPowerShell();
                return;
            }

            _powerShell.Commands.Clear();
            _powerShell.Streams.ClearStreams();
            _powerShell.Runspace.ResetRunspaceState();
        }

        public static Collection<PSObject> InvokeScript(
            string script,
            object[] args,
            out PSDataStreams dataStreams)
        {
            CheckPowerShell();

            var results = _powerShell.AddScript(script).AddParameters(args).Invoke();
            dataStreams = _powerShell.Streams;
            return results;
        }

        public static Collection<PSObject> InvokeCommand(
            string command,
            object[] args,
            out PSDataStreams dataStreams)
        {
            CheckPowerShell();

            var results = _powerShell.AddCommand(command).AddParameters(args).Invoke();
            dataStreams = _powerShell.Streams;
            return results;
        }
    }

    #endregion

    #endregion
}
