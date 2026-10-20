# SPDX-License-Identifier: GPL-3.0-or-later
# Build the app and run its hardware-free self-test on Windows.
# This is the same gate CI runs; it never opens a HID device.
$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "src/Retro87/Retro87.csproj"
$bench = Join-Path $root "tools/Retro87.Bench/Retro87.Bench.csproj"

Write-Host "== dotnet build (Release) =="
dotnet build $project -c Release --nologo
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "== Retro87.Bench --selftest =="
$output = & dotnet run --project $bench -c Release --nologo -- --selftest 2>&1
$output | Write-Host
$ok = ($output | Select-String -Pattern '^ok' -AllMatches).Count
$fail = ($output | Select-String -Pattern '^FAIL' -AllMatches).Count
Write-Host "ok:   $ok"
Write-Host "FAIL: $fail"
if ($LASTEXITCODE -ne 0 -or $fail -ne 0) {
    Write-Error "SELFTEST FAILED"
    exit 1
}
Write-Host "SELFTEST PASSED"

Write-Host "== dotnet test (xUnit, no hardware) =="
dotnet test (Join-Path $root "tests/Retro87.Tests/Retro87.Tests.csproj") -c Release --nologo
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Host "TESTS PASSED"
