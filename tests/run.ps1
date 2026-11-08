param([string]$OutputDirectory = "bin")

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
& (Join-Path $root "build.ps1") -OutputDirectory $OutputDirectory
$csc = Join-Path $env:windir "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$app = Join-Path (Join-Path $root $OutputDirectory) "keycap.exe"
$out = Join-Path (Join-Path $root $OutputDirectory) "modifier-tests.exe"
try {
    & $csc /nologo /target:exe "/out:$out" "/reference:$app" (Join-Path $PSScriptRoot "ModifierTests.cs")
    if ($LASTEXITCODE -ne 0) { throw "Test compilation failed" }
    & $out
    if ($LASTEXITCODE -ne 0) { throw "Modifier tests failed" }
}
finally {
    if (Test-Path -LiteralPath $out) { Remove-Item -LiteralPath $out -Force }
}
