[CmdletBinding()]
param(
	[string[]] $OnlyShape
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$project = Join-Path $root 'src/Spike/Spike.csproj'
$source = Join-Path $root 'src/Spike'
$models = Join-Path $source 'Models'
$migrations = Join-Path $source 'Migrations'
$artifacts = Join-Path $root 'artifacts'
$tools = Join-Path $root '.tools'
$ef = if ($IsWindows) { Join-Path $tools 'dotnet-ef.exe' } else { Join-Path $tools 'dotnet-ef' }

function Select-Model([string] $name) {
	Copy-Item -LiteralPath (Join-Path $models "Model.$name.cs") -Destination (Join-Path $source 'Model.cs') -Force
}

function Reset-Migrations {
	if (Test-Path -LiteralPath $migrations) {
		Remove-Item -LiteralPath $migrations -Recurse -Force
	}
}

function Add-Migration([string] $name) {
	& $ef migrations add $name --project $project --startup-project $project --output-dir Migrations
	if ($LASTEXITCODE -ne 0) { throw "dotnet ef migrations add $name failed." }
}

function Run-Shape([string] $shape, [string] $baseline, [string] $target, [bool] $useAnnotationProvider) {
	$mode = if ($useAnnotationProvider) { 'annotation-provider' } else { 'target-model-only' }
	$result = Join-Path $artifacts "$shape/$mode"
	if (Test-Path -LiteralPath $result) {
		Remove-Item -LiteralPath $result -Recurse -Force
	}
	New-Item -ItemType Directory -Force -Path $result | Out-Null
	Reset-Migrations
	Select-Model $baseline
	$env:SPIKE_USE_ANNOTATION_PROVIDER = if ($useAnnotationProvider) { '1' } else { '0' }
	$env:SPIKE_LOG_PATH = Join-Path $result 'operation-report.log'
	Remove-Item -LiteralPath $env:SPIKE_LOG_PATH -Force -ErrorAction SilentlyContinue
	Add-Migration 'Baseline'
	$baselineEvidence = Join-Path $result 'baseline'
	New-Item -ItemType Directory -Force -Path $baselineEvidence | Out-Null
	Get-ChildItem -LiteralPath $migrations -Filter '*Baseline*.cs' | Copy-Item -Destination $baselineEvidence -Force
	Select-Model $target
	Add-Migration $shape
	& $ef migrations script --project $project --startup-project $project --idempotent | Set-Content -LiteralPath (Join-Path $result 'migration.sql')
	if ($LASTEXITCODE -ne 0) { throw "dotnet ef migrations script for $shape failed." }
	Get-ChildItem -LiteralPath $migrations -Filter "*$shape*.cs" | Copy-Item -Destination $result -Force
	& $ef database drop --project $project --startup-project $project --force
	if ($LASTEXITCODE -ne 0) { throw "dotnet ef database drop for $shape failed." }
	& $ef database update --project $project --startup-project $project
	if ($LASTEXITCODE -ne 0) { throw "dotnet ef database update for $shape failed." }
	Get-Content -LiteralPath $env:SPIKE_LOG_PATH | Set-Content -LiteralPath (Join-Path $result 'operation-report.txt')
}

New-Item -ItemType Directory -Force -Path $artifacts | Out-Null
if (!(Test-Path -LiteralPath $ef)) {
	dotnet tool install dotnet-ef --tool-path $tools --version 11.0.0-preview.6.26359.118
	if ($LASTEXITCODE -ne 0) { throw 'dotnet-ef tool installation failed.' }
}

docker compose -f (Join-Path $root 'docker-compose.yml') up --detach --wait
if ($LASTEXITCODE -ne 0) { throw 'PostgreSQL container startup failed.' }

$shapes = @(
	@('Shape_1_Create', 'Empty', 'Create'),
	@('Shape_2_AddColumn', 'BaseTemporal', 'AddColumn'),
	@('Shape_3_RenameColumn', 'BaseTemporal', 'RenameColumn'),
	@('Shape_4_DropColumn', 'WithDescription', 'BaseTemporal'),
	@('Shape_5_AlterColumn', 'BaseTemporal', 'AlterColumn'),
	@('Shape_6_MarkerAdded', 'Unmarked', 'BaseTemporal'),
	@('Shape_7_MarkerRemoved', 'BaseTemporal', 'Unmarked')
)

foreach ($shape in $shapes) {
	if ($OnlyShape -and $shape[0] -notin $OnlyShape) { continue }
	Run-Shape $shape[0] $shape[1] $shape[2] $false
	Run-Shape $shape[0] $shape[1] $shape[2] $true
}

Remove-Item Env:SPIKE_USE_ANNOTATION_PROVIDER -ErrorAction SilentlyContinue
Remove-Item Env:SPIKE_LOG_PATH -ErrorAction SilentlyContinue
Write-Host "Spike evidence: $artifacts"
