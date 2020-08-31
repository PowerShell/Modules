# Copyright (c) Microsoft Corporation. All rights reserved.
# Licensed under the MIT License.

[CmdletBinding()]
param(
    [switch] $Clean = $false
)

Push-Location $PSScriptRoot/src/code
if ($Clean) {
    Remove-Item -Recurse -Path ./bin -Force
    Remove-Item -Recurse -Path ./obj -Force
}

dotnet build
Pop-Location
