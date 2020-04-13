# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

Wait-Debugger

$MyInvocation.MyCommand.Module | fl * -force

# Set up the module to deregister itself when removed
$MyInvocation.MyCommand.Module.OnRemove = {
    Write-Verbose "Deregistering UNIX native util completers"
}
