// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Management.Automation;
using System.Text;

namespace Microsoft.PowerShell.TextUtility
{
    [Cmdlet(VerbsData.ConvertFrom, "Base64")]
    [OutputType(typeof(string))]
    public sealed class ConvertFromBase64Command : PSCmdlet
    {
        /// <summary>
        /// Gets or sets the base64 encoded string.
        /// </summary>
        [Parameter(Position=0, Mandatory=true, ValueFromPipeline=true)]
        public string EncodedText { get; set; }

        protected override void ProcessRecord()
        {
            var base64Bytes = Convert.FromBase64String(EncodedText);
            WriteObject(Encoding.UTF8.GetString(base64Bytes));
        }
    }

    [Cmdlet(VerbsData.ConvertTo, "Base64")]
    [OutputType(typeof(string))]
    public sealed class ConvertToBase64Command : PSCmdlet
    {
        /// <summary>
        /// Gets or sets the text to encoded to base64.
        /// </summary>
        [Parameter(Position=0, Mandatory=true, ValueFromPipeline=true)]
        public string Text { get; set; }

        protected override void ProcessRecord()
        {
            var textBytes = Encoding.UTF8.GetBytes(Text);
            WriteObject(Convert.ToBase64String(textBytes));
        }
    }
}
