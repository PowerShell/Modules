# Copyright (c) Microsoft Corporation. All rights reserved.
# Licensed under the MIT License.

@{
   # Script module or binary module file associated with this manifest.
    RootModule = '.\Microsoft.PowerShell.TextUtility.dll'

    # Version number of this module.
    ModuleVersion = '1.0.0'

    # Supported PSEditions
    CompatiblePSEditions = @('Desktop', 'Core')

    # ID used to uniquely identify this module
    GUID = '5cb64356-cd04-4a18-90a4-fa4072126155'

    # Author of this module
    Author = 'Microsoft Corporation'

    # Company or vendor of this module
    CompanyName = 'Microsoft Corporation'

    # Copyright statement for this module
    Copyright = '(c) Microsoft Corporation. All rights reserved.'

    # Description of the functionality provided by this module
    Description = "This module contains cmdlets to help with manipulating or reading text."

    # Minimum version of the PowerShell engine required by this module
    PowerShellVersion = '5.1'

    # Format files (.ps1xml) to be loaded when importing this module
    FormatsToProcess = @('Microsoft.PowerShell.TextUtility.format.ps1xml')

    # Cmdlets to export from this module, for best performance, do not use wildcards and do not delete the entry, use an empty array if there are no cmdlets to export.
    CmdletsToExport = @(
        'Compare-Text','ConvertFrom-Base64','ConvertTo-Base64'
    )

    # Private data to pass to the module specified in RootModule/ModuleToProcess. This may also contain a PSData hashtable with additional module metadata used by PowerShell.
    PrivateData = @{

        PSData = @{

            # Tags applied to this module. These help with module discovery in online galleries.
            # Tags = @()

            # A URL to the license for this module.
            LicenseUri = 'https://github.com/PowerShell/Modules/License.txt'

            # A URL to the main website for this project.
            ProjectUri = 'https://github.com/PowerShell/Modules'

            # A URL to an icon representing this module.
            # IconUri = ''

            # ReleaseNotes of this module
            # ReleaseNotes = ''

            # Prerelease string of this module
            # Prerelease = ''

            # Flag to indicate whether the module requires explicit user acceptance for install/update/save
            # RequireLicenseAcceptance = $false

            # External dependent modules of this module
            # ExternalModuleDependencies = @()
        } # End of PSData hashtable

    } # End of PrivateData hashtable

    # HelpInfo URI of this module
    # HelpInfoURI = ''

}
