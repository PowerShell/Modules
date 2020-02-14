// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Language;
using System.Text;

namespace Microsoft.PowerShell.UnixCompleters
{
    public static class UnixUtilCompletion
    {
        private static string s_fullTypeName = typeof(UnixUtilCompletion).FullName;

        public static IEnumerable<CompletionResult> CompleteCommand(
            string command,
            string wordToComplete,
            CommandAst commandAst,
            int cursorPosition)
        {
            if (CompleterGlobals.UnixUtilCompleter == null)
            {
                return Enumerable.Empty<CompletionResult>();
            }

            return CompleterGlobals.UnixUtilCompleter.CompleteCommand(command, wordToComplete, commandAst, cursorPosition);
        }

        internal static ScriptBlock CreateInvocationScriptBlock(string command)
        {
            string script = new StringBuilder(256)
                .Append("param($wordToComplete,$commandAst,$cursorPosition)[")
                .Append(s_fullTypeName)
                .Append("]::")
                .Append(nameof(CompleteCommand))
                .Append("('")
                .Append(command)
                .Append("',$wordToComplete,$commandAst,$cursorPosition)")
                .ToString();

            return ScriptBlock.Create(script);
        }
    }
}