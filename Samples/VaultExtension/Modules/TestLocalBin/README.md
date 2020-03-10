# TestLocalBin

This is a simple binary module vault extension example.
It contains a PowerShell module manifest (TestLocalBin.psd1) and a C# source file and dotNet csproj file needed to build the binary assembly.
This module uses Export-CliXml/Import-CliXml cmdlets to store and retrieve secret objects to a file.  

This is **not** a secure storage, and is only intended as a vault extension example.  

You can experiment with this vault extension example by first building a TestLocalBin.dll binary assembly, and then copying this module to your local machine under a file path that PowerShell uses to discover modules (e.g., within a $env:PSModulePath path).  

You can then register this as a Secret Management local vault extension with the following command:  

```powershell
Register-SecretVault -Name CodeLocalVault -ModuleName TestLocalBin

Get-SecretVault CodeLocalVault

Name           ModuleName     ImplementingType
----           ----------     ----------------
CodeLocalVault TestLocalBin   CodeLocalVault.SecretsManagementExtension
```
