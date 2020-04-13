// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.ObjectModel;
using System.Management.Automation;

namespace Microsoft.PowerShell.UnixCompleters.Commands
{
    [Cmdlet(VerbsData.Import, Utils.ModulePrefix + "s")]
    public class ImportPSUnixUtilCompletersCommand : PSCmdlet
    {
        protected override void EndProcessing()
        {
            // Do nothing here; this command does its job by autoloading
        }
    }
}
