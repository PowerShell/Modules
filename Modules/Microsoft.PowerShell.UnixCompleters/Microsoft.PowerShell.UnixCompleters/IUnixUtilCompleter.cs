// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Management.Automation;
using System.Management.Automation.Language;

namespace Microsoft.PowerShell.UnixCompleters
{
    /// <summary>
    /// Provides completions for native Unix commands.
    /// </summary>
    public interface IUnixUtilCompleter
    {
        /// <summary>
        /// Gets the list of commands this completer can generate completions for.
        /// </summary>
        /// <returns>The names of all commands this completer can generate completions for.</returns>
        IEnumerable<string> FindCompletableCommands();

        /// <summary>
        /// Complete a given Unix command.
        /// </summary>
        /// <param name="command">
        /// The name of the command to complete.
        /// Guaranteed to be one of the strings output by FindCompletableCommand().
        /// </param>
        /// <param name="wordToComplete">The current word to complete.</param>
        /// <param name="commandAst">The whole command AST undergoing completion.</param>
        /// <param name="cursorPosition">The offset of the cursor from the start of input.</param>
        /// <returns>A list of completions for the current word.</returns>
        IEnumerable<CompletionResult> CompleteCommand(
            string command,
            string wordToComplete,
            CommandAst commandAst,
            int cursorPosition);
    }
}
