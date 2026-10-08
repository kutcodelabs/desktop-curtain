# Desktop Curtain launcher: compiles Curtain.cs in memory and runs it.
$ErrorActionPreference = 'Stop'
Add-Type -Path (Join-Path $PSScriptRoot 'Curtain.cs') -ReferencedAssemblies System.Windows.Forms, System.Drawing
[MasaustuPerdesi.Program]::Main()
