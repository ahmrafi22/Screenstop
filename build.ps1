[CmdletBinding()]
param(
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"

$solution = Join-Path $PSScriptRoot "Screenstop.sln"
$tests = Join-Path $PSScriptRoot "tests\Screenstop.Core.Tests"

dotnet restore $solution
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

dotnet build $solution --no-restore -c $Configuration
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

dotnet test $tests --no-build -c $Configuration
exit $LASTEXITCODE
