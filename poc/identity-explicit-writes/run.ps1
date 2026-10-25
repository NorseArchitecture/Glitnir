[CmdletBinding()]
param(
	[ValidateSet('G1', 'G2', 'G3', 'G4', 'G5', 'G6')]
	[string[]] $OnlyShape,
	[switch] $KeepContainers
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$project = Join-Path $root 'src/Spike/Spike.csproj'
$compose = Join-Path $root 'docker-compose.yml'
$artifacts = Join-Path $root 'artifacts'
$results = [System.Collections.Generic.List[pscustomobject]]::new()

function Add-Result([string] $step, [string] $status, [string] $detail) {
	$results.Add([pscustomobject]@{ Step = $step; Status = $status; Detail = $detail })
	Write-Host "$status $step $detail"
}

# Runs the spike with the given arguments, tees raw output to artifacts/<log>.log, and records every
# PASS/FAIL/PENDING line the spike prints. Returns the raw output lines.
function Invoke-Spike([string] $log, [string[]] $arguments) {
	$output = & dotnet run --no-build --project $project -- @arguments 2>&1 | ForEach-Object { "$_" }
	$exit = $LASTEXITCODE
	$output | Set-Content -LiteralPath (Join-Path $artifacts "$log.log")
	foreach ($line in $output) {
		if ($line -match '^(PASS|FAIL|PENDING) ([^\s:]+):? ?(.*)$') { Add-Result $Matches[2] $Matches[1] $Matches[3] }
	}
	if ($exit -ne 0 -and -not ($output -match '^FAIL ')) { Add-Result $log 'FAIL' "spike exited $exit; see artifacts/$log.log" }
	return $output
}

function Write-Summary {
	$results | Format-Table -AutoSize | Out-String | Set-Content -LiteralPath (Join-Path $artifacts 'summary.txt')
	$failed = @($results | Where-Object Status -eq 'FAIL').Count
	$pending = @($results | Where-Object Status -eq 'PENDING').Count
	$passed = @($results | Where-Object Status -eq 'PASS').Count
	Write-Host "PASS $passed / FAIL $failed / PENDING $pending — evidence: $artifacts"
	return $failed
}

if (Test-Path -LiteralPath $artifacts) { Remove-Item -LiteralPath $artifacts -Recurse -Force }
New-Item -ItemType Directory -Force -Path $artifacts | Out-Null

dotnet build $project --nologo 2>&1 | Set-Content -LiteralPath (Join-Path $artifacts 'build.log')
if ($LASTEXITCODE -ne 0) { throw 'Spike build failed; see artifacts/build.log.' }

docker compose -f $compose up --detach --wait 2>&1 | Set-Content -LiteralPath (Join-Path $artifacts 'compose-up.log')
if ($LASTEXITCODE -ne 0) { throw 'Container startup failed; see artifacts/compose-up.log.' }

try {
	# Engine floors before any gate: SQL Server 2025 (17) and PostgreSQL 19.
	$versions = Invoke-Spike 'versions' @('versions')
	$pgVersion = [int](($versions | Select-String '^postgres\.server_version_num=(\d+)$').Matches[0].Groups[1].Value)
	$sqlMajor = [int](($versions | Select-String '^sqlserver\.product_major_version=(\d+)$').Matches[0].Groups[1].Value)
	Add-Result 'postgres.version-floor' ($pgVersion -ge 190000 ? 'PASS' : 'FAIL') "server_version_num=$pgVersion (>= 190000)"
	Add-Result 'sqlserver.version-floor' ($sqlMajor -ge 17 ? 'PASS' : 'FAIL') "ProductMajorVersion=$sqlMajor (>= 17)"
	if ($pgVersion -lt 190000 -or $sqlMajor -lt 17) {
		Write-Summary | Out-Null
		throw 'Engine floor not met; aborting before any gate runs.'
	}

	Invoke-Spike 'ddl-postgres' @('apply-ddl', 'postgres', $artifacts) | Out-Null
	Invoke-Spike 'ddl-sqlserver' @('apply-ddl', 'sqlserver', $artifacts) | Out-Null
	if ($results | Where-Object Status -eq 'FAIL') {
		Write-Summary | Out-Null
		throw 'Schema did not apply cleanly; aborting before any gate runs.'
	}

	foreach ($gate in 'G1', 'G2', 'G3', 'G4', 'G5', 'G6') {
		if ($OnlyShape -and $gate -notin $OnlyShape) { continue }
		Invoke-Spike "gate-$gate" @('gate', $gate, $artifacts) | Out-Null
	}
}
finally {
	if (-not $KeepContainers) {
		docker compose -f $compose down -v 2>&1 | Set-Content -LiteralPath (Join-Path $artifacts 'compose-down.log')
	}
}

exit ((Write-Summary) -eq 0 ? 0 : 1)
