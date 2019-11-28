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