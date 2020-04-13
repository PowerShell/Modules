# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

# Set up the module to deregister itself when removed
$MyInvocation.MyCommand.Module.OnRemove = {
    Write-Verbose "Deregistering UNIX native util completers"
}
