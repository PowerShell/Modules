// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Management.Automation;

using DiffMatchPatch;

namespace Microsoft.PowerShell.TextUtility
{
    public struct CompareTextDiff
    {
        public List<Diff> Diff;
    }

    public enum CompareTextView
    {
        Inline,
        SideBySide,

    }

    [Cmdlet(VerbsData.Compare, "Text")]
    [OutputType(typeof(CompareTextDiff))]
    public sealed class CompareTextCommand : PSCmdlet
    {
        /// <summary>
        /// Gets or sets the left hand side text to compare.
        /// </summary>
        [Parameter(Position=0, Mandatory=true)]
        public string LeftText { get; set; }

        /// <summary>
        /// Gets or sets the right hand side text to compare.
        /// </summary>
        [Parameter(Position=1, Mandatory=true)]
        public string RightText { get; set; }

        /// <summary>
        /// Gets or sets the view type.
        /// </summary>
        [Parameter()]
        public CompareTextView View { get; set; }

        private string _leftFile = null;
        private string _rightFile = null;
        protected override void BeginProcessing()
        {
            try
            {
                string leftFile = SessionState.Path.GetUnresolvedProviderPathFromPSPath(LeftText);
                string rightFile = SessionState.Path.GetUnresolvedProviderPathFromPSPath(RightText);
                if (File.Exists(leftFile))
                {
                    _leftFile = leftFile;
                    LeftText = File.ReadAllText(_leftFile);
                }

                if (File.Exists(rightFile))
                {
                    _rightFile = rightFile;
                    RightText = File.ReadAllText(_rightFile);
                }
            }
            catch
            {
                // do nothing and treat as text
            }
        }

        protected override void ProcessRecord()
        {
            diff_match_patch dmp = new diff_match_patch();
            List<Diff> diff = dmp.diff_main(LeftText, RightText);
            dmp.diff_cleanupSemantic(diff);
            var output = new CompareTextDiff();
            output.Diff = diff;
            var psObj = new PSObject(output);

            if (_leftFile != null)
            {
                psObj.Properties.Add(new PSNoteProperty("LeftFile", _leftFile));
            }

            if (_rightFile != null)
            {
                psObj.Properties.Add(new PSNoteProperty("RightFile", _rightFile));
            }

            if (View == CompareTextView.SideBySide)
            {
                psObj.TypeNames.Insert(0, "Microsoft.PowerShell.TextUtility.CompareTextDiff#SideBySide");
            }

            WriteObject(psObj);
        }
    }
}
