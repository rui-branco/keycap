$ErrorActionPreference = "Stop"

$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$csc  = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"

if (-not (Test-Path $csc)) { throw "csc.exe not found at $csc" }

# Built under %TEMP%, not bin/: build.ps1 owns bin, and the app build globs
# src/*.cs only, so nothing in tests/ can leak into keycap.exe.
$outDir = Join-Path $env:TEMP "keycap-tests"
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir | Out-Null }
$out = Join-Path $outDir "DimMathTests.exe"

$args = @(
  "/nologo",
  "/target:exe",
  "/platform:anycpu",
  "/optimize+",
  "/codepage:65001",
  "/out:$out",
  "/reference:System.dll",
  (Join-Path $root "src\DimMath.cs"),
  (Join-Path $root "tests\DimMathTests.cs")
)

Write-Host "Compiling tests..."
& $csc $args
if ($LASTEXITCODE -ne 0) { throw "TEST BUILD FAILED (csc exit $LASTEXITCODE)" }

& $out
exit $LASTEXITCODE
