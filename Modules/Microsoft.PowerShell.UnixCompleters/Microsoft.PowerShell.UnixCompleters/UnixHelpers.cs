// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace Microsoft.PowerShell.UnixCompleters
{
    internal static class UnixHelpers
    {
        private readonly static IReadOnlyList<string> s_nativeUtilDirs = new []
        {
            "/usr/local/sbin",
            "/usr/local/bin",
            "/usr/sbin",
            "/usr/bin",
            "/sbin",
            "/bin"
        };

        private readonly static IReadOnlyDictionary<string, ShellType> s_shells = new Dictionary<string, ShellType>()
        {
            { "zsh", ShellType.Zsh },
            { "bash", ShellType.Bash },
        };

        private readonly static Lazy<IReadOnlyList<string>> s_nativeUtilNamesLazy =
            new Lazy<IReadOnlyList<string>>(GetNativeUtilNames);

        internal static IReadOnlyList<string> NativeUtilDirs => s_nativeUtilDirs;

        internal static IReadOnlyList<string> NativeUtilNames => s_nativeUtilNamesLazy.Value;

        internal static bool TryFindShell(string shellName, out string shellPath, out ShellType shellType)
        {
            // No shell name provided
            if (string.IsNullOrEmpty(shellName))
            {
                shellPath = null;
                shellType = ShellType.None;
                return false;
            }

            // Look for absolute path to a shell
            if (Path.IsPathRooted(shellName)
                && s_shells.TryGetValue(Path.GetFileName(shellName), out shellType)
                && File.Exists(shellName))
            {
                shellPath = shellName;
                return true;
            }

            // Now assume the shell is just a command name, and confirm we recognize it
            if (!s_shells.TryGetValue(shellName, out shellType))
            {
                shellPath = null;
                return false;
            }

            return TryFindShellByName(shellName, out shellPath);
        }

        internal static bool TryFindFallbackShell(out string foundShell, out ShellType shellType)
        {
            foreach (KeyValuePair<string, ShellType> shell in s_shells)
            {
                if (TryFindShellByName(shell.Key, out foundShell))
                {
                    shellType = shell.Value;
                    return true;
                }
            }

            foundShell = null;
            shellType = ShellType.None;
            return false;
        }

        private static bool TryFindShellByName(string shellName, out string foundShellPath)
        {
            foreach (string utilDir in UnixHelpers.NativeUtilDirs)
            {
                string shellPath = Path.Combine(utilDir, shellName);
                if (File.Exists(shellPath))
                {
                    foundShellPath = shellPath;
                    return true;
                }
            }

            foundShellPath = null;
            return false;
        }


        private static IReadOnlyList<string> GetNativeUtilNames()
        {
            var commandSet = new HashSet<string>(StringComparer.Ordinal);
            foreach (string utilDir in s_nativeUtilDirs)
            {
                if (Directory.Exists(utilDir))
                {
                    foreach (string utilPath in Directory.GetFiles(utilDir))
                    {
                        if (IsExecutable(utilPath))
                        {
                            commandSet.Add(Path.GetFileName(utilPath));
                        }
                    }
                }
            }
            var commandList = new List<string>(commandSet);
            return commandList;
        }

        private static bool IsExecutable(string path)
        {
            return access(path, X_OK) != -1;
        }

        private const int X_OK = 0x01;

        [DllImport("libc", CharSet = CharSet.Ansi, SetLastError = true)]
        private static extern int access(string pathname, int mode);
    }
}
