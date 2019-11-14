##
##
##

Write-Warning "Adding test module path"
$pathExists = $false
$modulePath = 'c:\temp\Modules'
foreach ($path in ($env:PSModulePath -split ';'))
{
    $pathExists = if ($path -eq $modulePath) { $true }
}
if (! $pathExists)
{
    Write-Warning "Adding module path."
    $env:PSModulePath += ";${modulePath}"
}
$env:PSModulePath -split ';'

Write-Warning "Copying test files..."
Copy-Item -Path E:\Git_Repos\PaulHigin\Modules\Modules\Microsoft.PowerShell.SecretsManagement\test\*.ps* -Dest $PWD
