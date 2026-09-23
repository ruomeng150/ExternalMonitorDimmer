#requires -Version 5.1
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$projectPath = Split-Path -Parent $PSScriptRoot
$testOutput = Join-Path $projectPath 'dist\regression-tests'
New-Item -ItemType Directory -Path $testOutput -Force | Out-Null
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$testExecutable = Join-Path $testOutput 'RegressionTests.exe'
$sources = @('NativeMethods.cs', 'WmiBrightnessProvider.cs', 'Storage.cs', 'MainForm.cs') |
    ForEach-Object { Join-Path $projectPath $_ }
$sources += Join-Path $PSScriptRoot 'RegressionTests.cs'
$arguments = @(
    '/nologo', '/target:exe', '/platform:anycpu', '/warn:4', '/warnaserror+', '/codepage:65001',
    ('/out:' + $testExecutable),
    '/reference:System.dll', '/reference:System.Core.dll', '/reference:System.Drawing.dll',
    '/reference:System.Windows.Forms.dll', '/reference:System.Xml.dll', '/reference:System.Management.dll'
) + $sources
& $compilerPath @arguments
if ($LASTEXITCODE -ne 0) { throw 'Regression test compilation failed.' }
& $testExecutable
if ($LASTEXITCODE -ne 0) { throw 'Regression tests failed.' }
