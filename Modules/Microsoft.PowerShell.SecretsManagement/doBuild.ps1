# Copyright (c) Microsoft Corporation. All rights reserved.
# Licensed under the MIT License.

<#
.DESCRIPTION
Implement build and packaging of the package and place the output $OutDirectory/$ModuleName
#>
function DoBuild
{
    Write-Verbose -Verbose -Message "Starting DoBuild with configuration: $BuildConfiguration, framework: $BuildFramework"

    # Module build out path
    $BuildOutPath = "${OutDirectory}/${ModuleName}"
    Write-Verbose -Verbose -Message "Module output file path: '$BuildOutPath'"

    # Module build source path
    $BuildSrcPath = "bin/${BuildConfiguration}/${BuildFramework}/publish"
    Write-Verbose -Verbose -Message "Module build source path: '$BuildSrcPath'"

    # copy psm1 and psd1 files
    copy-item "${SrcPath}/${ModuleName}.psd1" "${OutDirectory}/${ModuleName}"
    copy-item "${SrcPath}/${ModuleName}.psm1" "${OutDirectory}/${ModuleName}"

    # copy format files here
    #

    # copy help
    Write-Verbose -Verbose -Message "Copying help files to '$BuildOutPath'"
    copy-item -Recurse "${HelpPath}/${Culture}" "$BuildOutPath"

    if ( Test-Path "${SrcPath}/code" ) {
        Write-Verbose -Verbose -Message "Building assembly and copying to '$BuildOutPath'"
        # build code and place it in the staging location
        Push-Location "${SrcPath}/code"
        try {
            # Build source
            dotnet publish --configuration $BuildConfiguration --framework $BuildFramework

            # Place build results
            if (! (Test-Path -Path "$BuildSrcPath/${ModuleName}.dll"))
            {
                throw "Expected binary was not created: $BuildSrcPath/${ModuleName}.dll"
            }
            Copy-Item "$BuildSrcPath/${ModuleName}.dll" -Dest "$BuildOutPath"
            
            if (Test-Path -Path "$BuildSrcPath/${ModuleName}.pdb")
            {
                Copy-Item -Path "$BuildSrcPath/${ModuleName}.pdb" -Dest "$BuildOutPath"
            }
        }
        catch {
            Write-Error "dotnet build failed with error: $_"
        }
        finally {
            Pop-Location
        }
    }
    else {
        Write-Verbose -Verbose -Message "No code to build in '${SrcPath}/code'"
    }

    ## Add build and packaging here
    Write-Verbose -Verbose -Message "Ending DoBuild"
}
