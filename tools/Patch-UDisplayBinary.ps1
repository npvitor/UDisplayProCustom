<# Builds the color-only engine from the SHA-256-pinned original. Never stops or installs anything. #>
param(
    [string]$InputExe = "$PSScriptRoot\..\original\UDisplay_Original_v1.0.2.9.exe",
    [string]$OutputExe = "$PSScriptRoot\..\dist\UDisplay.exe",
    [string]$Settings
)
$ErrorActionPreference = 'Stop'
$configurator = Join-Path $PSScriptRoot '..\dist\UDisplayConfig.exe'
if (!(Test-Path -LiteralPath $configurator)) { throw 'Execute Build.bat primeiro.' }
$source = (Resolve-Path -LiteralPath $InputExe).Path
$destination = [IO.Path]::GetFullPath($OutputExe)
if ($source -eq $destination) { throw 'A saida nao pode substituir o original.' }
# The compiled builder validates the original SHA-256 before making any change.
$argsList = @('--build', ('"{0}"' -f $source), ('"{0}"' -f $destination))
if ($Settings) { $argsList += ('"{0}"' -f (Resolve-Path -LiteralPath $Settings).Path) }
$process = Start-Process -FilePath $configurator -ArgumentList $argsList -PassThru -Wait -WindowStyle Hidden
if ($process.ExitCode -ne 0) { throw "Falha de construcao. Consulte $destination.error.txt" }
Write-Host "Motor de cores construido: $destination"
