#Requires -Version 7
<#
.SYNOPSIS
  PostgreSQL-as-document-store reconnaissance runner. See README.md for the matrix.
.EXAMPLE
  ./run.ps1                # primary + replica up, run all SQL scripts, run the harness
  ./run.ps1 -Script 02     # run one matrix SQL script
  ./run.ps1 -SkipHarness   # SQL scripts only (no .NET SDK needed)
  ./run.ps1 -Down          # tear down (removes volumes)
#>
param(
	[string]$Script,
	[switch]$SkipHarness,
	[switch]$Down
)

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

if ($Down) {
	docker compose down -v
	return
}

# Official postgres:19beta2 (pinned in docker-compose.yml). Tag gone → compose fails
# loudly here; no silent fallback to PG18. The replica clones from the primary on first start
# (pg_basebackup) — if that fails it is the expected first place to need a machine-local tweak.
docker compose up -d --wait
if ($LASTEXITCODE -ne 0) { Write-Host 'primary/replica failed to start healthy' -ForegroundColor Red; exit 1 }

New-Item -ItemType Directory -Force results | Out-Null

$scripts = Get-ChildItem scripts -Filter '*.sql' | Sort-Object Name
if ($Script) {
	$scripts = $scripts | Where-Object { $_.Name -like "$Script*" }
	if (-not $scripts) { Write-Host "no script matches '$Script'" -ForegroundColor Red; exit 1 }
}

foreach ($s in $scripts) {
	$out = Join-Path results ($s.BaseName + '.out')
	Write-Host "── running $($s.Name) → $out" -ForegroundColor Cyan
	# All SQL scripts run on the primary. 03 inspects replication state from the primary side
	# (pg_stat_replication); the precise insert→visible timing is the harness's job.
	docker exec pg19doc-primary psql -U postgres -d norse_poc -v ON_ERROR_STOP=0 -f "/scripts/$($s.Name)" 2>&1 |
		Tee-Object -FilePath $out
}

if (-not $SkipHarness -and -not $Script) {
	$out = Join-Path results 'harness.out'
	Write-Host "── running harness (Npgsql, no EF) → $out" -ForegroundColor Cyan
	# Primary for writes (5455), replica for reads (5456). Connection strings passed as args so
	# the harness has zero embedded configuration.
	$primary = 'Host=localhost;Port=5455;Username=postgres;Database=norse_poc'
	$replica = 'Host=localhost;Port=5456;Username=postgres;Database=norse_poc'
	dotnet run --project harness -c Release -- --primary $primary --replica $replica 2>&1 |
		Tee-Object -FilePath $out

	# The ruled read path (2026-06-16): read-only EF translates predicate + projection
	# EXPRESSIONS to server-side jsonb SQL. Reads the replica; logs the generated SQL.
	$outEf = Join-Path results 'harness-ef.out'
	Write-Host "── running harness-ef (read-only EF → jsonb SQL) → $outEf" -ForegroundColor Cyan
	dotnet run --project harness-ef -c Release -- --replica $replica 2>&1 |
		Tee-Object -FilePath $outEf
}

Write-Host ''
Write-Host 'done — record conclusions in FINDINGS.md (dated against beta1; re-verify at RC1)' -ForegroundColor Green
