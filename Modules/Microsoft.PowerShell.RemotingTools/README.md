This module contains remoting tool cmdlets.

## EnableSSHRemoting

PowerShell SSH remoting was implemented in PowerShell 6.0 but requries SSH (client) and SSHD (service) components to be installed.
In addition the sshd_config configuration file must be updated to define a PowerShell endpoint as a subsystem.
Once this is done PowerShell remoting cmdlets can be used to establish a PowerShell remoting session over SSH that works across platforms. 

```powershell
$session = New-PSSession -HostName LinuxComputer1 -UserName UserA -SSHTransport
```

There are a number of requirements that must be satisfied for PowerShell SSH based remoting:

- PowerShell 6.0 or greater must be installed on the system.
Since multiple PowerShell installations can appear on a single system, a specific installation can be selected.
- SSH client must be installed on the system as PowerShell uses it for outgoing connections.
- SSHD (ssh daemon) must be installed on the system for PowerShell to receive SSH connections.
- SSHD must be configured with a Subsystem that serves as the PowerShell remoting endpoint.

This module exports a single cmdlet: Enable-SSHRemoting

The Enable-SSHRemoting cmdlet will do the following:

- Detect the underlying platform (Windows, Linux, macOS).
- Detect an installed SSH client, and emit a warning if not found.
- Detect an installed SSHD daemon, and emit a warning if not found.
- Accept a PowerShell (pwsh) path to be run as a remoting PowerShell session endpoint, or try to use the currently running PowerShell.
- Update the SSHD configuration file to add a PowerShell subsystem endpoint entry.

If all of the conditions are satisfied then PowerShell SSH remoting will work to and from the local system.
