$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root 'obj\tests'
New-Item -ItemType Directory -Force -Path $out | Out-Null
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
& "$framework\csc.exe" /nologo /out:"$out\EngineTests.exe" /r:"$root\dist\UDisplayConfig.exe" "$root\tests\EngineTests.cs"
if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed' }
Copy-Item -LiteralPath "$root\dist\UDisplayConfig.exe" -Destination $out -Force
& "$out\EngineTests.exe" "$root\original\UDisplay_Original_v1.0.2.9.exe" $out
if ($LASTEXITCODE -ne 0) { throw 'Tests failed' }
& "$framework\csc.exe" /nologo /out:"$out\ChromaGpuTests.exe" /r:"$root\dist\UDisplayConfig.exe" "$root\tests\ChromaGpuTests.cs"
if ($LASTEXITCODE -ne 0) { throw 'Chroma GPU test compilation failed' }
& "$out\ChromaGpuTests.exe"
if ($LASTEXITCODE -ne 0) { throw 'Chroma GPU tests failed' }
& "$framework\csc.exe" /nologo /platform:x86 /out:"$out\TrayTests.exe" "$root\tests\TrayTests.cs"
if ($LASTEXITCODE -ne 0) { throw 'Tray test compilation failed' }
& "$out\TrayTests.exe" "$root\dist\UDisplay.exe"
if ($LASTEXITCODE -ne 0) { throw 'Tray tests failed' }
