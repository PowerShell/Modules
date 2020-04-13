// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using SMA = System.Management.Automation;

namespace Microsoft.PowerShell.UnixCompleters
{
    public enum ShellType
    {
        None = 0,
        Zsh,
        Bash,
    }

    internal static class CompleterGlobals
    {
        private readonly static PropertyInfo s_executionContextProperty = typeof(Runspace).GetProperty("ExecutionContext", BindingFlags.NonPublic | BindingFlags.Instance);

        private readonly static PropertyInfo s_nativeArgumentCompletersProperty = s_executionContextProperty.PropertyType.GetProperty("NativeArgumentCompleters", BindingFlags.NonPublic | BindingFlags.Instance);

        private static Dictionary<string, ScriptBlock> s_nativeArgumentCompleterTable;

        internal static IEnumerable<string> CompletedCommands { get; set; }

        internal static Dictionary<string, ScriptBlock> NativeArgumentCompleterTable
        {
            get
            {
                if (s_nativeArgumentCompleterTable == null)
                {
                    object executionContext = s_executionContextProperty.GetValue(Runspace.DefaultRunspace);
                    
                    var completerTable = (Dictionary<string, ScriptBlock>)s_nativeArgumentCompletersProperty.GetValue(executionContext);

                    if (completerTable == null)
                    {
                        completerTable = new Dictionary<string, ScriptBlock>(StringComparer.OrdinalIgnoreCase);
                        s_nativeArgumentCompletersProperty.SetValue(executionContext, completerTable);
                    }

                    s_nativeArgumentCompleterTable = completerTable;
                }

                return s_nativeArgumentCompleterTable;
            }
        }

        internal static IUnixUtilCompleter UnixUtilCompleter { get; set; }
    }

    public class UtilCompleterInitializer : IModuleAssemblyInitializer
    {
        private const string SHELL_PREFERENCE_VARNAME = "COMPLETION_SHELL_PREFERENCE";

        public void OnImport()
        {
            string preferredCompletionShell = Environment.GetEnvironmentVariable(SHELL_PREFERENCE_VARNAME);

            ShellType shellType = ShellType.None;
            string shellExePath;
            if ((string.IsNullOrEmpty(preferredCompletionShell) || !UnixHelpers.TryFindShell(preferredCompletionShell, out shellExePath, out shellType))
                && !UnixHelpers.TryFindFallbackShell(out shellExePath, out shellType))
            {
                WriteError("Unable to find shell to provide unix utility completions");
                return;
            }

            IUnixUtilCompleter utilCompleter;
            switch (shellType)
            {
                case ShellType.Bash:
                    utilCompleter = new BashUtilCompleter(shellExePath);
                    break;

                case ShellType.Zsh:
                    utilCompleter = new ZshUtilCompleter(shellExePath);
                    break;

                default:
                    WriteError("Unable to find shell to provide unix utility completions");
                    return;
            }

            IEnumerable<string> utilsToComplete = utilCompleter.FindCompletableCommands();

            CompleterGlobals.CompletedCommands = utilsToComplete;
            CompleterGlobals.UnixUtilCompleter = utilCompleter;

            RegisterCompletersForCommands(utilsToComplete);
        }

        private void RegisterCompletersForCommands(IEnumerable<string> commands)
        {
            foreach (string command in commands)
            {
                CompleterGlobals.NativeArgumentCompleterTable[command] = UnixUtilCompletion.CreateInvocationScriptBlock(command);
            }
        }

        private static void OnRunspaceAvailable(object sender, RunspaceAvailabilityEventArgs args)
        {
            if (args.RunspaceAvailability != RunspaceAvailability.Available)
            {
                return;
            }

            var runspace = (Runspace)sender;

            runspace.SessionStateProxy.InvokeCommand.InvokeScript("Write-Host 'Hello'");
            runspace.AvailabilityChanged -= OnRunspaceAvailable;
        }
    }

    public class UtilCompleterCleanup : IModuleAssemblyCleanup
    {
        public void OnRemove(PSModuleInfo psModuleInfo)
        {
            foreach (string completedCommand in CompleterGlobals.CompletedCommands)
            {
                CompleterGlobals.NativeArgumentCompleterTable.Remove(completedCommand);
            }
        }
    }
}
