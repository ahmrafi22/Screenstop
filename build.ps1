[CmdletBinding()]
param(
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"

$solution = Join-Path $PSScriptRoot "Screendrop.sln"
$tests = Join-Path $PSScriptRoot "tests\Screendrop.Core.Tests"

dotnet restore $solution
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

dotnet build $solution --no-restore -c $Configuration
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

dotnet test $tests --no-build -c $Configuration
exit $LASTEXITCODE
