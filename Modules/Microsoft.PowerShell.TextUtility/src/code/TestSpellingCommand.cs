// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Management.Automation;

namespace Microsoft.PowerShell.TextUtility
{
    [Cmdlet(VerbsDiagnostic.Test, "Spelling")]
    [OutputType(typeof(string))]
    public sealed class TestSpellingCommand : PSCmdlet
    {
        /// <summary>
        /// Gets or sets the text to check spelling.
        /// </summary>
        [Parameter(Position=0, Mandatory=true)]
        public string Text { get; set; }

        private HashSet<string> _dictionary;

        protected override void BeginProcessing()
        {
            _dictionary = new HashSet<string>();
            using (var dictionary = new StreamReader(@"dictionary.txt"))
            {
                string line;
                while ((line = dictionary.ReadLine()) != null)
                {
                    _dictionary.Add(line);
                }
            }

            // TODO: use .spelling
        }

        protected override void ProcessRecord()
        {

        }
    }
}
