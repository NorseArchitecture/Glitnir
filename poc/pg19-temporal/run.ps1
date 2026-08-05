#Requires -Version 7
<#
.SYNOPSIS
  PG19 FOR PORTION OF reconnaissance runner. See README.md for the matrix.
.EXAMPLE
  ./run.ps1              # run all matrix scripts
  ./run.ps1 -Script 04   # run one matrix row
  ./run.ps1 -Down        # tear down the container
#>
param(
	[string]$Script,
	[switch]$Down
)

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

if ($Down) {
	docker compose down -v
	return
}

# Official postgres:19beta2 (pinned in docker-compose.yml). If the tag ever
# disappears, compose fails loudly here — no silent fallback to PG18; these scripts
# test PG19 behavior, period.
docker compose up -d --wait
if ($LASTEXITCODE -ne 0) { Write-Host 'container failed to start healthy' -ForegroundColor Red; exit 1 }

New-Item -ItemType Directory -Force results | Out-Null

$scripts = Get-ChildItem scripts -Filter '*.sql' | Sort-Object Name
if ($Script) {
	$scripts = $scripts | Where-Object { $_.Name -like "$Script*" }
	if (-not $scripts) { Write-Host "no script matches '$Script'" -ForegroundColor Red; exit 1 }
}

foreach ($s in $scripts) {
	$out = Join-Path results ($s.BaseName + '.out')
	Write-Host "── running $($s.Name) → $out" -ForegroundColor Cyan
	docker exec pg19-poc psql -U postgres -d norse_poc -v ON_ERROR_STOP=0 -f "/scripts/$($s.Name)" 2>&1 |
		Tee-Object -FilePath $out
}

Write-Host ''
Write-Host 'done — record conclusions in FINDINGS.md (dated against beta2; re-verify at RC1)' -ForegroundColor Green
