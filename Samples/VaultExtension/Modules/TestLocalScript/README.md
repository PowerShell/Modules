# TestLocalScript

This is a simple script module vault extension example which uses PowerShell's Export-CliXml/Import-CliXml cmdlets to store and retrieve secret objects to a file.  

This is **not** a secure storage, and is only intended as a vault extension example.  

You can experiment with this vault extension example by copying this module to your local machine under a file path that PowerShell uses to discover modules (e.g., within a $env:PSModulePath path).
You can then register this as a Secret Management local vault extension with the following command:  

```powershell
Register-SecretVault -Name ScriptLocalVault -ModuleName TestLocalScript

Get-SecretVault ScriptLocalVault

Name             ModuleName       ImplementingType
----             ----------       ----------------
ScriptLocalVault TestLocalScript
```
