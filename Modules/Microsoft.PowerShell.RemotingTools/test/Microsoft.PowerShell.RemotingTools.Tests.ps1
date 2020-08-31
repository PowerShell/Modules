# Copyright (c) Microsoft Corporation. All rights reserved.
# Licensed under the MIT License.

Describe "Test Microsoft.PowerShell.RemotingTools module" -tags CI {

    It "Module should export one command" {
        $commands = Get-Command -Module Microsoft.PowerShell.RemotingTools
        $commands.Count | Should -BeExactly 1
        $commands[0].Name | Should -Be "Enable-SSHRemoting"
    }
}
