#Requires -Version 7
<#
.SYNOPSIS
	Verifies the MSBuild enforcement law lands and behaves.
	Spec: docs/superpowers/specs/2026-06-05-build-enforcement-design.md
	Exit 0 = law verified. Exit 1 = at least one assertion failed.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$pocRoot = $PSScriptRoot
$script:failures = [System.Collections.Generic.List[string]]::new()

function Write-Check([string] $message) {
	Write-Host "CHECK  $message"
}

function Add-Failure([string] $message) {
	$script:failures.Add($message)
	Write-Host "FAIL   $message" -ForegroundColor Red
}

# Expected-value prefixes: '!~' = must not contain, '~' = contains, '!' = must differ, '' (empty string) = must be empty.
# $expected must carry at least two keys: a single -getProperty returns plain text, not JSON.
function Assert-Properties([string] $project, [hashtable] $expected) {
	$names = ($expected.Keys | Sort-Object) -join ','
	$raw = dotnet msbuild (Join-Path $pocRoot $project) "-getProperty:$names" 2>&1 | Out-String
	if ($LASTEXITCODE -ne 0) {
		Add-Failure "$project property evaluation failed:`n$raw"
		return
	}
	# SDK banners (NETSDK1057, workload nags) can precede the JSON in CI environments — extract the object before parsing.
	$jsonText = [regex]::Match($raw, '\{.*\}', [System.Text.RegularExpressions.RegexOptions]::Singleline).Value
	if (-not $jsonText) {
		Add-Failure "$project -getProperty returned no JSON:`n$raw"
		return
	}
	$json = $jsonText | ConvertFrom-Json
	foreach ($name in $expected.Keys) {
		$want = [string]$expected[$name]
		$actual = [string]$json.Properties.$name
		# switch -Wildcard has no implicit break: a value like '!~CS1591' matches BOTH '!~*' and '!*',
		# and the last matching branch would win. 'break' makes first-match-wins real, so order holds.
		$ok = switch -Wildcard ($want) {
			'!~*' { $actual -notlike "*$($want.Substring(2))*"; break }
			'~*' { $actual -like "*$($want.Substring(1))*"; break }
			'!*' { $actual -ne $want.Substring(1); break }
			default { $actual -eq $want }
		}
		if ($ok) { Write-Check "$project  $name = '$actual'" }
		else { Add-Failure "$project  $name expected '$want', got '$actual'" }
	}
}

function Assert-CleanBuild([string] $project) {
	$out = dotnet build (Join-Path $pocRoot $project) -tl:off --nologo 2>&1 | Out-String
	if ($LASTEXITCODE -eq 0) { Write-Check "$project  clean build passed" }
	else { Add-Failure "$project clean build failed:`n$out" }
}

function Assert-CanaryBuild([string] $project, [string[]] $expectedIds, [string] $toggle = 'EnableCanaries') {
	$out = dotnet build (Join-Path $pocRoot $project) -tl:off --nologo "-p:$toggle=true" 2>&1 | Out-String
	if ($LASTEXITCODE -eq 0) {
		Add-Failure "$project canaried build unexpectedly succeeded"
		return
	}
	foreach ($id in $expectedIds) {
		if ($out -match "error $id") { Write-Check "$project  canary $id fired as error" }
		elseif ($out -match "warning $id") { Add-Failure "$project canary $id fired as WARNING - ratchet broken" }
		else { Add-Failure "$project canary $id did not fire" }
	}
}

$law = @{
	AnalysisLevel = 'latest-Recommended'
	AnalysisLevelSecurity = 'latest-All'
	AnalysisLevelPerformance = 'latest-All'
	AnalysisLevelReliability = 'latest-All'
	AnalysisLevelUsage = 'latest-All'
	TreatWarningsAsErrors = 'true'
	GenerateDocumentationFile = 'true'
	UseArtifactsOutput = 'true'
}

Write-Host "`n=== src/ - full law ==="
Assert-Properties 'src/Glitnir.Probe/Glitnir.Probe.csproj' ($law + @{ JsonSerializerIsReflectionEnabledByDefault = 'false' })
Assert-CleanBuild 'src/Glitnir.Probe/Glitnir.Probe.csproj'
Assert-CanaryBuild 'src/Glitnir.Probe/Glitnir.Probe.csproj' @(
	# Phase 1 set — unchanged
	'CA5394', 'CA1810', 'CA2007', 'CA2201', 'CA2200', 'CS0219', 'CS8618',
	# Phase 2 style law
	'IDE0161',  # block-scoped namespace
	'IDE0055',  # space-indented line (formatting law)
	'IDE0007',  # explicit type where var is law (built-in and apparent buckets)
	# IDE0008 unasserted: unreachable under the all-var buckets (re-ruled 2026-06-06);
	# the construction form (`var x = new T();`) is YGG analyzer bench territory
	'IDE0090',  # new T() where new() is law
	'IDE0040',  # redundant accessibility modifier
	'IDE1006',  # naming law (m_-prefixed field)
	'IDE0005',  # gratuitous using — the drive-by ratchet
	'IDE0305',  # fluent .ToList() with explicit collection target
	# Phase 2 CA reach-proofs
	'CA1727',   # lowercase log placeholder (targeted editorconfig severity)
	'CA1848',   # LoggerMessage delegates (proves Performance latest-All reaches it — no editorconfig line)
	'CA2254',   # interpolated log template (proves Usage latest-All reaches it — no editorconfig line)
	# CA1852 (seal internal types) fires here only because the law sets
	# dotnet_code_quality.CA1852.ignore_internalsvisibleto = true. This assembly grants
	# InternalsVisibleTo (src/Directory.Build.props, §2.3), under which CA1852 self-disables
	# by default (a friend could derive); the option overrides that — tests consume
	# internals, they don't derive from them. See FINDINGS.md deviation #12.
	'CA1852'
)
Assert-CanaryBuild 'src/Glitnir.Probe/Glitnir.Probe.csproj' @('CS1591') 'EnableDocCanaries'

Write-Host "`n=== src/ - razor probe (law lands; lawful razor builds clean) ==="
Assert-Properties 'src/Glitnir.Probe.Components/Glitnir.Probe.Components.csproj' $law
Assert-CleanBuild 'src/Glitnir.Probe.Components/Glitnir.Probe.Components.csproj'

Write-Host "`n=== tests/ - law + NoWarn delta + IVT ==="
Assert-Properties 'tests/Glitnir.Probe.Tests/Glitnir.Probe.Tests.csproj' ($law + @{ NoWarn = '~CS1591'; IsPackable = 'false'; JsonSerializerIsReflectionEnabledByDefault = '' })
Assert-CleanBuild 'tests/Glitnir.Probe.Tests/Glitnir.Probe.Tests.csproj'
Assert-CanaryBuild 'tests/Glitnir.Probe.Tests/Glitnir.Probe.Tests.csproj' @('CA2200')

Write-Host "`n=== benchmarks/ - law + NoWarn delta ==="
Assert-Properties 'benchmarks/Glitnir.Probe.Benchmarks/Glitnir.Probe.Benchmarks.csproj' ($law + @{ NoWarn = '~CS1591' })
Assert-CleanBuild 'benchmarks/Glitnir.Probe.Benchmarks/Glitnir.Probe.Benchmarks.csproj'
Assert-CanaryBuild 'benchmarks/Glitnir.Probe.Benchmarks/Glitnir.Probe.Benchmarks.csproj' @('CA2200')

Write-Host "`n=== poc/ - severed ==="
Assert-Properties 'poc/Glitnir.Probe.Severed/Glitnir.Probe.Severed.csproj' @{
	AnalysisLevelSecurity = ''
	TreatWarningsAsErrors = '!true'
	UseArtifactsOutput = 'true'
	TargetFramework = 'net11.0'
}
Assert-CleanBuild 'poc/Glitnir.Probe.Severed/Glitnir.Probe.Severed.csproj'

Write-Host "`n=== tests/smoke/ - severed + AOT floor ==="
Assert-Properties 'tests/smoke/Glitnir.Probe.Smoke/Glitnir.Probe.Smoke.csproj' @{
	PublishAot = 'true'
	AnalysisLevelSecurity = ''
	NoWarn = '!~CS1591'
	IsPackable = '!false'
}
Assert-CleanBuild 'tests/smoke/Glitnir.Probe.Smoke/Glitnir.Probe.Smoke.csproj'

if ($script:failures.Count -gt 0) {
	Write-Host "`n$($script:failures.Count) assertion(s) failed." -ForegroundColor Red
	exit 1
}
Write-Host "`nAll enforcement assertions passed." -ForegroundColor Green
exit 0
