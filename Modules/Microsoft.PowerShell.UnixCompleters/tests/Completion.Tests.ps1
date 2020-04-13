# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

Describe 'Microsoft.PowerShell.UnixCompleters completion tests' {
    BeforeAll {
        Import-Module "$PSScriptRoot/../out/Microsoft.PowerShell.UnixCompleters"
    }

    Context "Zsh completions" {
        BeforeAll {
            $completionTestCases = @(
                @{ InStr = 'ls -a'; CursorPos = 5; Suggestions = @('-aA', '-ad', '-aL', '-aR', '-ah', '-ai', '-al', '-ag', '-a1', '-aC', '-am', '-ax', '-as', '-ac', '-au', '-ar', '-at', '-aF', '-ap', '-an', '-ab', '-aq', '-ak', '-aS', '-aT', '-ao', '-af', '-aw', '-aB', '-aG', '-aH', '-aP') }
                @{ InStr = 'grep --'; CursorPos = 7; Suggestions = @('--include', '--exclude', '--exclude-from', '--exclude-dir') }
                @{ InStr = 'dd i'; CursorPos = 4; Suggestions = @('if=', 'ibs=') }
                @{ InStr = 'cat -'; CursorPos = 5; Suggestions = @('-b', '-e', '-n', '-s', '-t', '-u', '-v') }
                @{ InStr = 'ps au'; CursorPos = 5; Suggestions = @('aue', 'aux', 'auw', 'auH', 'auL', 'auS', 'auT', 'auZ', 'auA', 'auC', 'auc', 'auh', 'aum', 'aur') }
            )
        }

        It "Completes '<inStr>' correctly" -TestCases $completionTestCases {
            param($InStr, $CursorPos, $Suggestions)

            $result = TabExpansion2 -inputScript $InStr -cursorColumn $CursorPos

            foreach ($s in $Suggestions)
            {
                $result.CompletionMatches.CompletionText | Should -Contain $s
            }
        }
    }
}
