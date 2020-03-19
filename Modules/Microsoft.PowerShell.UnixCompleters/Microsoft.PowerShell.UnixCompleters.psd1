@{

# Script module or binary module file associated with this manifest.
RootModule = 'Microsoft.PowerShell.UnixCompleters.dll'

# Version number of this module.
ModuleVersion = '0.1.1'

# Supported PSEditions
CompatiblePSEditions = 'Core'

# ID used to uniquely identify this module
GUID = '042bff5f-9644-43ef-8f4e-d8b8ed5a1f97'

# Author of this module
Author = 'Microsoft Corporation'

# Company or vendor of this module
CompanyName = 'Microsoft Corporation'

# Copyright statement for this module
Copyright = '© Microsoft Corporation'

# Description of the functionality provided by this module
Description = 'Get parameter completion for native Unix utilities. Requires zsh or bash.'

# Minimum version of the PowerShell engine required by this module
PowerShellVersion = '6.0'

# Script files (.ps1) that are run in the caller's environment prior to importing this module.
ScriptsToProcess = @("OnStart.ps1")

# Functions to export from this module, for best performance, do not use wildcards and do not delete the entry, use an empty array if there are no functions to export.
FunctionsToExport = @()

# Cmdlets to export from this module, for best performance, do not use wildcards and do not delete the entry, use an empty array if there are no cmdlets to export.
CmdletsToExport = @(
    'Import-UnixCompleters',
    'Remove-UnixCompleters',
    'Set-UnixCompleter'
)

# Variables to export from this module
VariablesToExport = @()

# Aliases to export from this module, for best performance, do not use wildcards and do not delete the entry, use an empty array if there are no aliases to export.
AliasesToExport = @()

# Private data to pass to the module specified in RootModule/ModuleToProcess. This may also contain a PSData hashtable with additional module metadata used by PowerShell.
    PrivateData = @{

        PSData = @{

            # A URL to the license for this module.
            LicenseUri = 'https://raw.githubusercontent.com/PowerShell/Modules/master/LICENSE'

            # A URL to the main website for this project.
            ProjectUri = 'https://github.com/PowerShell/Modules/tree/master/Modules/Microsoft.PowerShell.UnixCompleters'

            # Flag to indicate whether the module requires explicit user acceptance for install/update/save
            RequireLicenseAcceptance = $false

        } # End of PSData hashtable

    } # End of PrivateData hashtable
}
