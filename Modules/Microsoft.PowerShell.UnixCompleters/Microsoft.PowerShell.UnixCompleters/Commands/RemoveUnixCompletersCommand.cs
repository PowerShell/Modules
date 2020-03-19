// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Management.Automation;

namespace Microsoft.PowerShell.UnixCompleters.Commands
{
    [Cmdlet(VerbsCommon.Remove, Utils.ModulePrefix + "s")]
    public class RemoveUnixCompletersCommand : PSCmdlet
    {
        protected override void EndProcessing()
        {
            InvokeCommand.InvokeScript("Remove-Module -Name PSUnixUtilCompleters");
        }
    }
}
