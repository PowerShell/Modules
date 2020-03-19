// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Management.Automation;

namespace Microsoft.PowerShell.UnixCompleters.Commands
{
    [Cmdlet(VerbsCommon.Set, Utils.ModulePrefix)]
    public class SetUnixCompleterCommand : Cmdlet
    {
        [ValidateNotNull()]
        [Parameter(Position = 0, Mandatory = true, ParameterSetName = "Completer")]
        public IUnixUtilCompleter Completer { get; set; }

        [ValidateNotNullOrEmpty()]
        [Parameter(Position = 0, Mandatory = true, ParameterSetName = "Shell")]
        public string Shell { get; set; }

        [ValidateNotNullOrEmpty()]
        [Parameter(Position = 0, Mandatory = true, ParameterSetName = "ShellType")]
        [Parameter(Position = 1, ParameterSetName = "Shell")]
        public ShellType ShellType { get; set; }

        protected override void EndProcessing()
        {
            if (Completer == null)
            {
                string shellName = Shell ?? ShellType.ToString().ToLower();

                if (!UnixHelpers.TryFindShell(shellName, out string shellPath, out ShellType shellType))
                {
                    ThrowTerminatingError(
                        new ErrorRecord(
                            new ItemNotFoundException($"Unable to find shell '{shellName}'"),
                            "CompletionShellNotFound",
                            ErrorCategory.ObjectNotFound,
                            shellName));
                    return;
                }

                // Allow a scenario where a shell is named one thing but behaves as another
                // and the user has manually specified what shell it behaves as
                if (ShellType != ShellType.None)
                {
                    shellType = ShellType;
                }

                switch (shellType)
                {
                    case ShellType.Zsh:
                        Completer = new ZshUtilCompleter(shellPath);
                        break;

                    case ShellType.Bash:
                        Completer = new BashUtilCompleter(shellPath);
                        break;

                    default:
                        ThrowTerminatingError(
                            new ErrorRecord(
                                new ArgumentException($"Unable to create a completer for shell type '{shellType}'"),
                                "InvalidCompletionShellType",
                                ErrorCategory.InvalidArgument,
                                shellType));
                        return;
                }
            }

            CompleterGlobals.UnixUtilCompleter = Completer;
        }
    }
}
