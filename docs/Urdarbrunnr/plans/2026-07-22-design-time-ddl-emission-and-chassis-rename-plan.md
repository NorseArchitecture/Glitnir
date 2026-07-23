# EF Design-Time Chassis Rename + DDL Emission Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rename Urðarbrunnr's `Persistence.EntityFramework.Migrations*` chassis to `Persistence.EntityFramework.Design*` and add design-time DDL-emission (a decorated `IMigrationsScaffolder` that writes the current-state schema to a checked-in `.sql` file on every `dotnet ef migrations add`/`remove`), scoped entirely to the Urðarbrunnr repository.

**Architecture:** A mechanical rename of 9 projects (6 `src`, 3 `tests`) preserves all existing behavior under new names. New capability — `DdlEmittingMigrationsScaffolder`, `DesignTimeSchemaPath`, and `NorseDesignTimeServicesExtensions.AddNorseDesignTimeServices()` — lands in the renamed `Persistence.EntityFramework.Design` project; two new provider-specific `IDesignTimeDbContextFactory<TContext>` bases (`NorsePostgreSqlDesignTimeDbContextFactory<T>` / `NorseSqlServerDesignTimeDbContextFactory<T>`) land in `Design.PostgreSQL` / `Design.SqlServer`, each exposing a `ConfigureOptions` virtual hook so a future ASP.NET Identity-style consumer can layer in `UseApplicationServiceProvider` without bypassing the base.

**Tech Stack:** .NET 11 preview / C# latest, EF Core 11 preview (`Microsoft.EntityFrameworkCore.Design`), xUnit v3 + Shouldly on Microsoft.Testing.Platform, `Microsoft.EntityFrameworkCore.Sqlite` (test-only, for exercising real `GenerateCreateScript()` output without a live server).

## Global Constraints

- **Scope is Urðarbrunnr only.** Do not touch Mímisbrunnr, Himinbjörg, or Yggdrasil in this plan. Yggdrasil's own `NorseRef` to this chassis is already stale on its current branch (`feature/hosting-web-server-authn`, unrelated in-flight work) — that is a pre-existing condition, not something this plan introduces or fixes.
- **No automatic git commits beyond the feature branch itself.** Commit on the local Urðarbrunnr feature branch as each task completes (subagents may commit on an unpushed feature branch the human is watching) — never push, merge, or open a PR from within this plan.
- **`internal sealed` by default; `omit_if_default` for accessibility.** New types are `internal`/no modifier unless an external assembly must consume them (the two factory bases and `NorseDesignTimeServicesExtensions` are `public`; everything else stays unmarked/internal).
- **No `.ToTable()` / `.HasDatabaseName()` naming overloads** in any new or touched entity configuration — not applicable to this plan's own code (no entities are added), noted for completeness.
- **Tabs for indentation**, US English spelling, `var` for return assignments only.
- **`IsAotCompatible = false`** stays set on every project in this chassis (EF Core Design-time tooling is not AOT/trim-compatible) — do not remove it.
- Full design context: `../Glitnir/docs/Urdarbrunnr/specs/2026-07-22-design-time-ddl-emission-and-chassis-rename-design.md`.

---

## Task 1: Rename the chassis (mechanical, zero behavior change)

**Files:**
- Rename (via `git mv`, directory + `.csproj`):
  - `src/Persistence.EntityFramework.Migrations/` → `src/Persistence.EntityFramework.Design/`
  - `src/Persistence.EntityFramework.Migrations.PostgreSQL/` → `src/Persistence.EntityFramework.Design.PostgreSQL/`
  - `src/Persistence.EntityFramework.Migrations.PostgreSQL.Generator/` → `src/Persistence.EntityFramework.Design.PostgreSQL.Generator/`
  - `src/Persistence.EntityFramework.Migrations.SqlServer/` → `src/Persistence.EntityFramework.Design.SqlServer/`
  - `src/Persistence.EntityFramework.Migrations.SqlServer.Generator/` → `src/Persistence.EntityFramework.Design.SqlServer.Generator/`
  - `src/Persistence.EntityFramework.Migrations.Generator.Shared/` → `src/Persistence.EntityFramework.Design.Generator.Shared/`
  - `tests/Persistence.EntityFramework.Migrations.Tests/` → `tests/Persistence.EntityFramework.Design.Tests/`
  - `tests/Persistence.EntityFramework.Migrations.PostgreSQL.Generator.Tests/` → `tests/Persistence.EntityFramework.Design.PostgreSQL.Generator.Tests/`
  - `tests/Persistence.EntityFramework.Migrations.SqlServer.Generator.Tests/` → `tests/Persistence.EntityFramework.Design.SqlServer.Generator.Tests/`
- Modify (text substitution only, no logic change): every `.cs`/`.csproj` file inside the renamed directories, plus `Urdarbrunnr.slnx`.

**Interfaces:**
- Produces: the renamed project/namespace surface every later task builds on — `Norse.Persistence.EntityFramework.Design` (namespace, was `...Migrations`), `Norse.Persistence.EntityFramework.Design.PostgreSQL`, `Norse.Persistence.EntityFramework.Design.SqlServer`, `Norse.Persistence.EntityFramework.Design.Generator.Shared`. `EfMigrationContributor<TContext>` and `MigrationConnectionStringAttribute` keep their exact names, only their namespace changes.

- [ ] **Step 1: Create the feature branch**

```bash
git -C /home/buvy/code/NorseArchitecture/Bifrost/Urdarbrunnr status --short
git -C /home/buvy/code/NorseArchitecture/Bifrost/Urdarbrunnr checkout -b feature/design-time-ddl-emission-and-chassis-rename
```
Expected: clean status (nothing to stash) and `Switched to a new branch 'feature/design-time-ddl-emission-and-chassis-rename'`.

- [ ] **Step 2: Rename the six `src` directories and their `.csproj` files**

```bash
cd() { :; }  # placeholder guard; use explicit -C paths below, do not actually alias cd
U=/home/buvy/code/NorseArchitecture/Bifrost/Urdarbrunnr

git -C "$U" mv src/Persistence.EntityFramework.Migrations src/Persistence.EntityFramework.Design
git -C "$U" mv src/Persistence.EntityFramework.Design/Persistence.EntityFramework.Migrations.csproj \
              src/Persistence.EntityFramework.Design/Persistence.EntityFramework.Design.csproj

git -C "$U" mv src/Persistence.EntityFramework.Migrations.PostgreSQL src/Persistence.EntityFramework.Design.PostgreSQL
git -C "$U" mv src/Persistence.EntityFramework.Design.PostgreSQL/Persistence.EntityFramework.Migrations.PostgreSQL.csproj \
              src/Persistence.EntityFramework.Design.PostgreSQL/Persistence.EntityFramework.Design.PostgreSQL.csproj

git -C "$U" mv src/Persistence.EntityFramework.Migrations.PostgreSQL.Generator src/Persistence.EntityFramework.Design.PostgreSQL.Generator
git -C "$U" mv src/Persistence.EntityFramework.Design.PostgreSQL.Generator/Persistence.EntityFramework.Migrations.PostgreSQL.Generator.csproj \
              src/Persistence.EntityFramework.Design.PostgreSQL.Generator/Persistence.EntityFramework.Design.PostgreSQL.Generator.csproj

git -C "$U" mv src/Persistence.EntityFramework.Migrations.SqlServer src/Persistence.EntityFramework.Design.SqlServer
git -C "$U" mv src/Persistence.EntityFramework.Design.SqlServer/Persistence.EntityFramework.Migrations.SqlServer.csproj \
              src/Persistence.EntityFramework.Design.SqlServer/Persistence.EntityFramework.Design.SqlServer.csproj

git -C "$U" mv src/Persistence.EntityFramework.Migrations.SqlServer.Generator src/Persistence.EntityFramework.Design.SqlServer.Generator
git -C "$U" mv src/Persistence.EntityFramework.Design.SqlServer.Generator/Persistence.EntityFramework.Migrations.SqlServer.Generator.csproj \
              src/Persistence.EntityFramework.Design.SqlServer.Generator/Persistence.EntityFramework.Design.SqlServer.Generator.csproj

git -C "$U" mv src/Persistence.EntityFramework.Migrations.Generator.Shared src/Persistence.EntityFramework.Design.Generator.Shared
```
Expected: each `git mv` exits 0 with no output. Ignore the `cd` line above — it is a no-op guard, every command uses `git -C` explicitly and never changes the shell's working directory.

- [ ] **Step 3: Rename the three `tests` directories and their `.csproj` files**

```bash
U=/home/buvy/code/NorseArchitecture/Bifrost/Urdarbrunnr

git -C "$U" mv tests/Persistence.EntityFramework.Migrations.Tests tests/Persistence.EntityFramework.Design.Tests
git -C "$U" mv tests/Persistence.EntityFramework.Design.Tests/Persistence.EntityFramework.Migrations.Tests.csproj \
              tests/Persistence.EntityFramework.Design.Tests/Persistence.EntityFramework.Design.Tests.csproj

git -C "$U" mv tests/Persistence.EntityFramework.Migrations.PostgreSQL.Generator.Tests tests/Persistence.EntityFramework.Design.PostgreSQL.Generator.Tests
git -C "$U" mv tests/Persistence.EntityFramework.Design.PostgreSQL.Generator.Tests/Persistence.EntityFramework.Migrations.PostgreSQL.Generator.Tests.csproj \
              tests/Persistence.EntityFramework.Design.PostgreSQL.Generator.Tests/Persistence.EntityFramework.Design.PostgreSQL.Generator.Tests.csproj

git -C "$U" mv tests/Persistence.EntityFramework.Migrations.SqlServer.Generator.Tests tests/Persistence.EntityFramework.Design.SqlServer.Generator.Tests
git -C "$U" mv tests/Persistence.EntityFramework.Design.SqlServer.Generator.Tests/Persistence.EntityFramework.Migrations.SqlServer.Generator.Tests.csproj \
              tests/Persistence.EntityFramework.Design.SqlServer.Generator.Tests/Persistence.EntityFramework.Design.SqlServer.Generator.Tests.csproj
```
Expected: each `git mv` exits 0, no output.

- [ ] **Step 4: Substitute every remaining `Persistence.EntityFramework.Migrations` occurrence**

This rewrites namespace declarations, `using` statements, `.csproj` `<Description>` text, `<ProjectReference>`/`<Compile Include>` relative paths, and the embedded test-source strings inside `MigrationContributorGeneratorTests.cs` — all of them contain this exact substring.

```bash
U=/home/buvy/code/NorseArchitecture/Bifrost/Urdarbrunnr

grep -rlZ 'Persistence\.EntityFramework\.Migrations' "$U/src" "$U/tests" "$U/Urdarbrunnr.slnx" \
  | xargs -0 sed -i 's/Persistence\.EntityFramework\.Migrations/Persistence.EntityFramework.Design/g'

grep -rn 'Persistence\.EntityFramework\.Migrations' "$U/src" "$U/tests" "$U/Urdarbrunnr.slnx"
```
Expected: the `sed` command produces no output. The final verification `grep` produces **no matches** (exit code 1) — if it prints any line, the substitution missed an occurrence; re-run `sed` targeted at that file.

- [ ] **Step 5: Confirm the rename left no old-named files or stray references**

```bash
U=/home/buvy/code/NorseArchitecture/Bifrost/Urdarbrunnr

find "$U/src" "$U/tests" -iname "*Migrations*" | grep -v /obj/ | grep -v /bin/
git -C "$U" status --short
```
Expected: the `find` produces no output (no file or directory anywhere under `src`/`tests` still contains "Migrations" in its name). `git status --short` shows only `R` (renamed) and `M` (modified) entries — no untracked leftovers.

- [ ] **Step 6: Build and run every existing test to confirm zero behavior change**

```bash
dotnet build /home/buvy/code/NorseArchitecture/Bifrost/Urdarbrunnr/Urdarbrunnr.slnx
dotnet test /home/buvy/code/NorseArchitecture/Bifrost/Urdarbrunnr/Urdarbrunnr.slnx
```
Expected: build succeeds with 0 errors. All existing tests pass (0 failed) — this is the same test suite that passed before the rename; a rename-only change must not alter a single assertion's outcome. If anything fails, the failure is almost certainly a missed text substitution (Step 4) or a stale path (Step 4's slnx pass) — fix and re-run before proceeding.

- [ ] **Step 7: Commit**

```bash
U=/home/buvy/code/NorseArchitecture/Bifrost/Urdarbrunnr
git -C "$U" add -A
git -C "$U" commit -m "$(cat <<'EOF'
Rename Migrations chassis to Design

Persistence.EntityFramework.Migrations* becomes Persistence.EntityFramework.Design*
across src, tests, and Urdarbrunnr.slnx. Pure rename -- EfMigrationContributor<T>,
MigrationConnectionStringAttribute, and the AddNorseMigrations() generator keep
their exact behavior under the new namespace. Sets up the DDL-emission capability
landing in the same assemblies in a later task.
EOF
)"
git -C "$U" status --short
```
Expected: commit succeeds; `git status --short` shows a clean working tree.

---

## Task 2: Sync cross-repo references and documentation

**Files:**
- Modify: `/home/buvy/code/NorseArchitecture/Bifrost/Bifrost.slnx` (lines referencing the renamed projects)
- Modify: `/home/buvy/code/NorseArchitecture/Bifrost/Urdarbrunnr/README.md`
- Modify: `/home/buvy/code/NorseArchitecture/Bifrost/Urdarbrunnr/CLAUDE.md`

**Interfaces:**
- Consumes: the renamed project paths from Task 1 (`src/Persistence.EntityFramework.Design*`, `tests/Persistence.EntityFramework.Design*`).
- Produces: a Bifröst solution that still loads and a doc pair (`README.md`/`CLAUDE.md`) that matches the code, per this repo's boy-scout law.

- [ ] **Step 1: Update `Bifrost.slnx`'s project paths**

```bash
B=/home/buvy/code/NorseArchitecture/Bifrost
sed -i 's/Persistence\.EntityFramework\.Migrations/Persistence.EntityFramework.Design/g' "$B/Bifrost.slnx"
grep -n "Urdarbrunnr/src/Persistence\|Urdarbrunnr/tests/Persistence" "$B/Bifrost.slnx"
```
Expected: the `grep` output shows every Urðarbrunnr `Persistence.EntityFramework.Design*` entry (base, `.PostgreSQL`, `.PostgreSQL.Generator`, `.SqlServer`, `.SqlServer.Generator`, `.Generator.Shared` file, plus the three `.Tests`/`.Generator.Tests` entries) — none should still read `...Migrations...`.

- [ ] **Step 2: Confirm Bifröst's own composed solution still loads**

```bash
dotnet sln /home/buvy/code/NorseArchitecture/Bifrost/Bifrost.slnx list | grep -i "urdarbrunnr"
```
Expected: lists the Urðarbrunnr projects under their new `Persistence.EntityFramework.Design*` names, with no error from `dotnet sln`. (This lists projects only — do not run a full `dotnet build Bifrost.slnx`; Mímisbrunnr/Himinbjörg/Yggdrasil still reference the old names on their own branches and are expected to fail to restore until their own follow-on pass, per this plan's Global Constraints. That is out of scope here.)

- [ ] **Step 3: Update Urðarbrunnr's own `README.md`**

Read the current text first:

```bash
grep -n "Migrations" /home/buvy/code/NorseArchitecture/Bifrost/Urdarbrunnr/README.md
```

Then replace the two paragraphs (identified in the design doc, §2) naming the old projects. Use the Edit tool with this exact substitution — old text:

```
**Live:** `Norse.Persistence.EntityFramework`, `Norse.Persistence.EntityFramework.PostgreSQL`, `Norse.Persistence.EntityFramework.Migrations`, and `Norse.Persistence.EntityFramework.Migrations.PostgreSQL` — four assemblies shipped as part of the platform-wide migrations framework proven end to end across six realms (the full story is on [Bifröst's README](https://github.com/NorseArchitecture/Bifrost#readme)). The last of the four ships this realm's first Roslyn source generator: it discovers every EF migration contributor at compile time and emits `AddNorseMigrations()`, proven identical whether contributors arrive by `ProjectReference` or `PackageReference`.
```

new text:

```
**Live:** `Norse.Persistence.EntityFramework`, `Norse.Persistence.EntityFramework.PostgreSQL`, `Norse.Persistence.EntityFramework.Design`, and `Norse.Persistence.EntityFramework.Design.PostgreSQL` — four assemblies shipped as part of the platform-wide migrations framework proven end to end across six realms (the full story is on [Bifröst's README](https://github.com/NorseArchitecture/Bifrost#readme)). Renamed from `*.Migrations*` to `*.Design*` (2026-07-22) to reflect what this chassis actually is — EF design-time tooling, not the migrations themselves, which live downstream in each realm's own `.Migrations` project. The last of the four ships this realm's first Roslyn source generator: it discovers every EF migration contributor at compile time and emits `AddNorseMigrations()`, proven identical whether contributors arrive by `ProjectReference` or `PackageReference`. It also now ships `DdlEmittingMigrationsScaffolder`: on every `dotnet ef migrations add`/`remove`, it writes the current-state schema as plain DDL to a checked-in `.sql` file — reviewable by anyone fluent in SQL, not just C#. Full design: [Glitnir's `docs/Urdarbrunnr/specs/2026-07-22-design-time-ddl-emission-and-chassis-rename-design.md`](https://github.com/NorseArchitecture/Glitnir/blob/master/docs/Urdarbrunnr/specs/2026-07-22-design-time-ddl-emission-and-chassis-rename-design.md).
```

old text:

```
A SQL Server-parallel trio — `Norse.Persistence.EntityFramework.SqlServer`, `Norse.Persistence.EntityFramework.Migrations.SqlServer`, and its generator — landed alongside them, mirroring the Postgres packages exactly.
```

new text:

```
A SQL Server-parallel trio — `Norse.Persistence.EntityFramework.SqlServer`, `Norse.Persistence.EntityFramework.Design.SqlServer`, and its generator — landed alongside them, mirroring the Postgres packages exactly.
```

- [ ] **Step 4: Update Urðarbrunnr's own `CLAUDE.md`**

```bash
grep -n "Migrations" /home/buvy/code/NorseArchitecture/Bifrost/Urdarbrunnr/CLAUDE.md
```

Apply the same `*.Migrations*` → `*.Design*` substitution to every matching bullet in the "Four assemblies are live" list and the "SQL Server-parallel trio" paragraph, using the Edit tool (mirror Step 3's before/after text exactly against whatever the current file contains — read it first, since the header narrative may have shifted since this plan was written). Add one sentence to the end of the "Four assemblies are live" paragraph: `DdlEmittingMigrationsScaffolder landed in the same pass (2026-07-22) -- see the design doc for the mechanism.`

- [ ] **Step 5: Final sweep for any other stale reference inside Urðarbrunnr**

```bash
grep -rn "Persistence\.EntityFramework\.Migrations" /home/buvy/code/NorseArchitecture/Bifrost/Urdarbrunnr --include="*.md" --include="*.slnx" --include="*.csproj" --include="*.cs" | grep -v /obj/ | grep -v /bin/
```
Expected: no output. If anything appears, it is a doc or file this plan's research missed — fix it inline before committing.

- [ ] **Step 6: Commit**

```bash
U=/home/buvy/code/NorseArchitecture/Bifrost/Urdarbrunnr
git -C "$U" add -A
git -C "$U" commit -m "$(cat <<'EOF'
Sync README/CLAUDE.md with the Design chassis rename

Boy-scout law: the doc pair names the same projects the code does.
EOF
)"

B=/home/buvy/code/NorseArchitecture/Bifrost
git -C "$B" add Bifrost.slnx
git -C "$B" status --short
```
Expected: Urðarbrunnr commit succeeds. Bifröst's `Bifrost.slnx` change is staged only (per this repo's "no automatic git commits" law) — leave it staged for the human to review and commit.

---

## Task 3: DDL-emission mechanism

**Files:**
- Modify: `src/Persistence.EntityFramework.Design/Persistence.EntityFramework.Design.csproj` (add `Microsoft.EntityFrameworkCore.Design` package reference)
- Create: `src/Persistence.EntityFramework.Design/DesignTimeSchemaPath.cs`
- Create: `src/Persistence.EntityFramework.Design/DdlEmittingMigrationsScaffolder.cs`
- Create: `src/Persistence.EntityFramework.Design/NorseDesignTimeServicesExtensions.cs`
- Modify: `tests/Persistence.EntityFramework.Design.Tests/Persistence.EntityFramework.Design.Tests.csproj` (add `Microsoft.EntityFrameworkCore.Sqlite` test-only package)
- Create: `tests/Persistence.EntityFramework.Design.Tests/DesignTimeSchemaPathTests.cs`
- Create: `tests/Persistence.EntityFramework.Design.Tests/FakeMigrationsScaffolder.cs`
- Create: `tests/Persistence.EntityFramework.Design.Tests/DdlEmittingMigrationsScaffolderTests.cs`
- Create: `tests/Persistence.EntityFramework.Design.Tests/NorseDesignTimeServicesExtensionsTests.cs`

**Interfaces:**
- Consumes: `Norse.Persistence.EntityFramework.NorseDbContext`, `INorseEntity<TSelf>` (existing, from `Persistence.EntityFramework`), `Microsoft.EntityFrameworkCore.Infrastructure.ICurrentDbContext` (`Context` property, type `DbContext`), `Microsoft.EntityFrameworkCore.Migrations.Design.IMigrationsScaffolder` (verified live signature: `ScaffoldMigration(string migrationName, string? rootNamespace, string? subNamespace = null, string? language = null, bool dryRun = false)`, `RemoveMigration(string projectDir, string? rootNamespace, bool force, string? language, bool dryRun = false, bool offline = false)`, `Save(string projectDir, ScaffoldedMigration migration, string? outputDir, bool dryRun = false)`), `ScaffoldedMigration` (9-string constructor: `fileExtension, previousMigrationId, migrationCode, migrationId, metadataCode, migrationSubNamespace, snapshotCode, snapshotName, snapshotSubNamespace`), `MigrationFiles` (parameterless constructor).
- Produces: `DesignTimeSchemaPath.Resolve(string outputBaseDirectory, string databaseName) : string` (internal, pure function). `DdlEmittingMigrationsScaffolder` (internal, implements `IMigrationsScaffolder`, constructor `(IMigrationsScaffolder inner, ICurrentDbContext currentContext, string outputFilePath)`). `NorseDesignTimeServicesExtensions.AddNorseDesignTimeServices(this IServiceCollection services, string databaseName) : IServiceCollection` (public) — later tasks and the deferred downstream pass call this from a realm's own `IDesignTimeServices` implementation.

- [ ] **Step 1: Add the `Microsoft.EntityFrameworkCore.Design` package reference**

Read the current csproj:

```bash
cat /home/buvy/code/NorseArchitecture/Bifrost/Urdarbrunnr/src/Persistence.EntityFramework.Design/Persistence.EntityFramework.Design.csproj
```

Use the Edit tool to add this `PackageReference` inside the existing `<ItemGroup>`, alongside the existing `<NorseRef>`/`<ProjectReference>` items:

```xml
		<PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="11.*-*">
			<IncludeAssets>compile</IncludeAssets>
			<PrivateAssets>all</PrivateAssets>
		</PackageReference>
```

`IncludeAssets=compile` + `PrivateAssets=all`: this assembly needs `IMigrationsScaffolder`/`ScaffoldedMigration` types only at compile time — `dotnet ef`'s own tooling host supplies the real `Microsoft.EntityFrameworkCore.Design.dll` at runtime, and this dependency must never appear in a consumer's own package manifest.

- [ ] **Step 2: Write the failing test for `DesignTimeSchemaPath`**

```bash
cat > /home/buvy/code/NorseArchitecture/Bifrost/Urdarbrunnr/tests/Persistence.EntityFramework.Design.Tests/DesignTimeSchemaPathTests.cs << 'EOF'
namespace Norse.Persistence.EntityFramework.Design.Tests;

public sealed class DesignTimeSchemaPathTests
{
	[Fact]
	void Resolve_walks_up_three_levels_from_build_output_to_project_root()
	{
		var buildOutput = Path.Combine("repo", "Realm.Migrations.PostgreSQL", "bin", "Debug", "net10.0");

		var result = DesignTimeSchemaPath.Resolve(buildOutput, "norse_referencedata");

		result.ShouldBe(Path.Combine("repo", "Realm.Migrations.PostgreSQL", "schema", "norse_referencedata.sql"));
	}

	[Fact]
	void Resolve_throws_when_the_base_directory_is_too_shallow_to_have_a_project_root()
	{
		var buildOutput = Path.Combine("bin", "Debug");

		Should.Throw<InvalidOperationException>(() => DesignTimeSchemaPath.Resolve(buildOutput, "norse_referencedata"));
	}
}
EOF
```

- [ ] **Step 3: Run it to verify it fails**

```bash
dotnet test /home/buvy/code/NorseArchitecture/Bifrost/Urdarbrunnr/tests/Persistence.EntityFramework.Design.Tests
```
Expected: build FAILS with `CS0246: The type or namespace name 'DesignTimeSchemaPath' could not be found`.

- [ ] **Step 4: Implement `DesignTimeSchemaPath`**

```bash
cat > /home/buvy/code/NorseArchitecture/Bifrost/Urdarbrunnr/src/Persistence.EntityFramework.Design/DesignTimeSchemaPath.cs << 'EOF'
namespace Norse.Persistence.EntityFramework.Design;

/// <summary>
/// Resolves the checked-in schema file's path from a design-time-tooling build output directory
/// (<c>AppContext.BaseDirectory</c>, standard <c>bin/{Configuration}/{TargetFramework}/</c> layout).
/// A pure string operation -- <see cref="Directory.GetParent(string)"/> never touches the filesystem,
/// so this is safe to call before the target directory exists and safe to unit test without one.
/// </summary>
static class DesignTimeSchemaPath
{
	internal static string Resolve(string outputBaseDirectory, string databaseName)
	{
		var projectRoot = Directory.GetParent(outputBaseDirectory)?.Parent?.Parent?.FullName
			?? throw new InvalidOperationException(
				$"Could not resolve a project root three directory levels above '{outputBaseDirectory}'. " +
				"Expected a standard bin/{Configuration}/{TargetFramework} build output layout.");

		return Path.Combine(projectRoot, "schema", $"{databaseName}.sql");
	}
}
EOF
```

- [ ] **Step 5: Run the test to verify it passes**

```bash
dotnet test /home/buvy/code/NorseArchitecture/Bifrost/Urdarbrunnr/tests/Persistence.EntityFramework.Design.Tests
```
Expected: PASS, 2 tests (both `DesignTimeSchemaPathTests` cases), 0 failed.

- [ ] **Step 6: Write the failing test for `DdlEmittingMigrationsScaffolder`, starting with its test double**

```bash
cat > /home/buvy/code/NorseArchitecture/Bifrost/Urdarbrunnr/tests/Persistence.EntityFramework.Design.Tests/FakeMigrationsScaffolder.cs << 'EOF'
using Microsoft.EntityFrameworkCore.Migrations.Design;

namespace Norse.Persistence.EntityFramework.Design.Tests;

/// <summary>
/// Records calls instead of doing real scaffolding -- shared by <see cref="DdlEmittingMigrationsScaffolderTests"/>
/// and <see cref="NorseDesignTimeServicesExtensionsTests"/>, which both need to verify a call reached
/// EF's (fake) original registration.
/// </summary>
sealed class FakeMigrationsScaffolder : IMigrationsScaffolder
{
	public int ScaffoldMigrationCallCount { get; private set; }
	public int RemoveMigrationCallCount { get; private set; }
	public int SaveCallCount { get; private set; }

	public ScaffoldedMigration ScaffoldMigration(string migrationName, string? rootNamespace, string? subNamespace = null, string? language = null, bool dryRun = false)
	{
		ScaffoldMigrationCallCount++;
		return new ScaffoldedMigration("cs", null, "", "20260722000000_Test", "", "", "", "", "");
	}

	public MigrationFiles RemoveMigration(string projectDir, string? rootNamespace, bool force, string? language, bool dryRun = false, bool offline = false)
	{
		RemoveMigrationCallCount++;
		return new MigrationFiles();
	}

	public MigrationFiles Save(string projectDir, ScaffoldedMigration migration, string? outputDir, bool dryRun = false)
	{
		SaveCallCount++;
		return new MigrationFiles();
	}
}
EOF
```

Then the test file:

```bash
cat > /home/buvy/code/NorseArchitecture/Bifrost/Urdarbrunnr/tests/Persistence.EntityFramework.Design.Tests/DdlEmittingMigrationsScaffolderTests.cs << 'EOF'
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Migrations.Design;
using Microsoft.Extensions.DependencyInjection;

namespace Norse.Persistence.EntityFramework.Design.Tests;

public sealed class DdlEmittingMigrationsScaffolderTests
{
	[Fact]
	void ScaffoldMigration_delegates_to_inner_scaffolder_and_returns_its_result()
	{
		FakeMigrationsScaffolder inner = new();
		using var ctx = CreateContext();
		var outputPath = TempFilePath();
		DdlEmittingMigrationsScaffolder sut = new(inner, CurrentDbContext(ctx), outputPath);

		try
		{
			var result = sut.ScaffoldMigration("Initial", "MyNamespace");

			inner.ScaffoldMigrationCallCount.ShouldBe(1);
			result.MigrationId.ShouldBe("20260722000000_Test");
		}
		finally
		{
			File.Delete(outputPath);
		}
	}

	[Fact]
	void ScaffoldMigration_writes_a_ddl_file_headed_with_the_auto_generated_banner()
	{
		FakeMigrationsScaffolder inner = new();
		using var ctx = CreateContext();
		var outputPath = TempFilePath();
		DdlEmittingMigrationsScaffolder sut = new(inner, CurrentDbContext(ctx), outputPath);

		try
		{
			sut.ScaffoldMigration("Initial", "MyNamespace");

			var written = File.ReadAllText(outputPath);
			written.ShouldContain("AUTO-GENERATED BY EF CORE MIGRATIONS");
			written.ShouldContain("CREATE TABLE");
			written.ShouldContain("StubEntities");
		}
		finally
		{
			File.Delete(outputPath);
		}
	}

	[Fact]
	void ScaffoldMigration_creates_the_output_directory_when_it_does_not_exist()
	{
		FakeMigrationsScaffolder inner = new();
		using var ctx = CreateContext();
		var outputDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
		var outputPath = Path.Combine(outputDir, "schema.sql");
		DdlEmittingMigrationsScaffolder sut = new(inner, CurrentDbContext(ctx), outputPath);

		try
		{
			sut.ScaffoldMigration("Initial", "MyNamespace");

			Directory.Exists(outputDir).ShouldBeTrue();
			File.Exists(outputPath).ShouldBeTrue();
		}
		finally
		{
			Directory.Delete(outputDir, recursive: true);
		}
	}

	[Fact]
	void RemoveMigration_delegates_to_inner_scaffolder_and_re_emits_ddl()
	{
		FakeMigrationsScaffolder inner = new();
		using var ctx = CreateContext();
		var outputPath = TempFilePath();
		DdlEmittingMigrationsScaffolder sut = new(inner, CurrentDbContext(ctx), outputPath);

		try
		{
			sut.RemoveMigration("projectDir", "MyNamespace", force: false, language: "C#");

			inner.RemoveMigrationCallCount.ShouldBe(1);
			File.Exists(outputPath).ShouldBeTrue();
		}
		finally
		{
			File.Delete(outputPath);
		}
	}

	[Fact]
	void Save_delegates_to_inner_scaffolder_without_re_emitting_ddl()
	{
		FakeMigrationsScaffolder inner = new();
		using var ctx = CreateContext();
		var outputPath = TempFilePath();
		DdlEmittingMigrationsScaffolder sut = new(inner, CurrentDbContext(ctx), outputPath);
		ScaffoldedMigration migration = new("cs", null, "", "id", "", "", "", "", "");

		sut.Save("projectDir", migration, null);

		inner.SaveCallCount.ShouldBe(1);
		File.Exists(outputPath).ShouldBeFalse();
	}

	static string TempFilePath() =>
		Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.sql");

	static StubContext CreateContext() =>
		new(new DbContextOptionsBuilder<StubContext>().UseSqlite("Data Source=:memory:").Options);

	static ICurrentDbContext CurrentDbContext(DbContext ctx) =>
		((IInfrastructure<IServiceProvider>)ctx).Instance.GetRequiredService<ICurrentDbContext>();

	sealed class StubContext(DbContextOptions<StubContext> options) : NorseDbContext(options)
	{
		public DbSet<StubEntity> StubEntities => Set<StubEntity>();
	}

	sealed class StubEntity : INorseEntity<StubEntity>
	{
		public int Id { get; set; }

		public static void Configure(EntityTypeBuilder<StubEntity> builder)
		{
		}
	}
}
EOF
```

- [ ] **Step 7: Add the `Microsoft.EntityFrameworkCore.Sqlite` test-only package**

Use the Edit tool on `tests/Persistence.EntityFramework.Design.Tests/Persistence.EntityFramework.Design.Tests.csproj` to add, alongside the existing `Microsoft.EntityFrameworkCore.InMemory` reference:

```xml
		<PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" Version="11.*-*" />
```

SQLite (not InMemory) because `Database.GenerateCreateScript()` requires a real relational provider — InMemory throws `NotSupportedException` for it, and SQLite needs no live server.

- [ ] **Step 8: Run the new tests to verify they fail**

```bash
dotnet test /home/buvy/code/NorseArchitecture/Bifrost/Urdarbrunnr/tests/Persistence.EntityFramework.Design.Tests
```
Expected: build FAILS with `CS0246: The type or namespace name 'DdlEmittingMigrationsScaffolder' could not be found`.

- [ ] **Step 9: Implement `DdlEmittingMigrationsScaffolder`**

```bash
cat > /home/buvy/code/NorseArchitecture/Bifrost/Urdarbrunnr/src/Persistence.EntityFramework.Design/DdlEmittingMigrationsScaffolder.cs << 'EOF'
using System.Text;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations.Design;

namespace Norse.Persistence.EntityFramework.Design;

/// <summary>
/// Decorates EF Core's own <see cref="IMigrationsScaffolder"/>: every <see cref="ScaffoldMigration"/>
/// or <see cref="RemoveMigration"/> call also writes the current-state schema as plain DDL to
/// <paramref name="outputFilePath"/> (via the constructor), so a DBA fluent in SQL but not C# can
/// review the schema without reading migration Designer files. Raw <c>GenerateCreateScript()</c>
/// passthrough -- no schema-guard cleanup, since nothing compiles this file yet (no <c>Microsoft.Build.Sql</c>
/// project consumes it; see the design doc, deferred until a realm actually needs one).
/// </summary>
sealed class DdlEmittingMigrationsScaffolder(
	IMigrationsScaffolder inner,
	ICurrentDbContext currentContext,
	string outputFilePath) : IMigrationsScaffolder
{
	public ScaffoldedMigration ScaffoldMigration(string migrationName, string? rootNamespace, string? subNamespace = null, string? language = null, bool dryRun = false)
	{
		var result = inner.ScaffoldMigration(migrationName, rootNamespace, subNamespace, language, dryRun);
		EmitDdl();
		return result;
	}

	public MigrationFiles RemoveMigration(string projectDir, string? rootNamespace, bool force, string? language, bool dryRun = false, bool offline = false)
	{
		var result = inner.RemoveMigration(projectDir, rootNamespace, force, language, dryRun, offline);
		EmitDdl();
		return result;
	}

	public MigrationFiles Save(string projectDir, ScaffoldedMigration migration, string? outputDir, bool dryRun = false) =>
		inner.Save(projectDir, migration, outputDir, dryRun);

	void EmitDdl()
	{
		var directory = Path.GetDirectoryName(outputFilePath);
		if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
			Directory.CreateDirectory(directory);

		StringBuilder sb = new(
			"""
			-- ============================================================
			-- AUTO-GENERATED BY EF CORE MIGRATIONS — DO NOT EDIT BY HAND
			-- Changes made here will be overwritten on the next migration.
			-- Run: dotnet ef migrations add <Name> to update this file.
			-- ============================================================

			""");
		sb.Append(currentContext.Context.Database.GenerateCreateScript());

		File.WriteAllText(outputFilePath, sb.ToString(), Encoding.UTF8);
	}
}
EOF
```

- [ ] **Step 10: Run the tests to verify they pass**

```bash
dotnet test /home/buvy/code/NorseArchitecture/Bifrost/Urdarbrunnr/tests/Persistence.EntityFramework.Design.Tests
```
Expected: PASS — all 5 `DdlEmittingMigrationsScaffolderTests` cases plus the 2 `DesignTimeSchemaPathTests` cases, 0 failed.

- [ ] **Step 11: Write the failing test for `AddNorseDesignTimeServices`**

```bash
cat > /home/buvy/code/NorseArchitecture/Bifrost/Urdarbrunnr/tests/Persistence.EntityFramework.Design.Tests/NorseDesignTimeServicesExtensionsTests.cs << 'EOF'
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Migrations.Design;
using Microsoft.Extensions.DependencyInjection;

namespace Norse.Persistence.EntityFramework.Design.Tests;

public sealed class NorseDesignTimeServicesExtensionsTests
{
	[Fact]
	void AddNorseDesignTimeServices_wraps_the_already_registered_scaffolder()
	{
		using var ctx = CreateContext();
		ServiceCollection services = new();
		FakeMigrationsScaffolder efScaffolder = new();
		services.AddSingleton<IMigrationsScaffolder>(efScaffolder);
		services.AddSingleton(CurrentDbContext(ctx));

		services.AddNorseDesignTimeServices("test-db");
		using var provider = services.BuildServiceProvider();

		var scaffolder = provider.GetRequiredService<IMigrationsScaffolder>();

		scaffolder.ShouldBeOfType<DdlEmittingMigrationsScaffolder>();
	}

	[Fact]
	void AddNorseDesignTimeServices_resolved_scaffolder_still_calls_through_to_ef_original()
	{
		using var ctx = CreateContext();
		ServiceCollection services = new();
		FakeMigrationsScaffolder efScaffolder = new();
		services.AddSingleton<IMigrationsScaffolder>(efScaffolder);
		services.AddSingleton(CurrentDbContext(ctx));

		services.AddNorseDesignTimeServices("test-db");
		using var provider = services.BuildServiceProvider();
		var scaffolder = provider.GetRequiredService<IMigrationsScaffolder>();

		scaffolder.ScaffoldMigration("Initial", "MyNamespace");

		efScaffolder.ScaffoldMigrationCallCount.ShouldBe(1);
	}

	[Fact]
	void AddNorseDesignTimeServices_throws_when_ef_has_not_registered_a_scaffolder()
	{
		using var ctx = CreateContext();
		ServiceCollection services = new();
		services.AddSingleton(CurrentDbContext(ctx));

		services.AddNorseDesignTimeServices("test-db");
		using var provider = services.BuildServiceProvider();

		Should.Throw<InvalidOperationException>(() => provider.GetRequiredService<IMigrationsScaffolder>());
	}

	static StubContext CreateContext() =>
		new(new DbContextOptionsBuilder<StubContext>().UseSqlite("Data Source=:memory:").Options);

	static ICurrentDbContext CurrentDbContext(DbContext ctx) =>
		((IInfrastructure<IServiceProvider>)ctx).Instance.GetRequiredService<ICurrentDbContext>();

	sealed class StubContext(DbContextOptions<StubContext> options) : NorseDbContext(options)
	{
		public DbSet<StubEntity> StubEntities => Set<StubEntity>();
	}

	sealed class StubEntity : INorseEntity<StubEntity>
	{
		public int Id { get; set; }

		public static void Configure(EntityTypeBuilder<StubEntity> builder)
		{
		}
	}
}
EOF
```

- [ ] **Step 12: Run it to verify it fails**

```bash
dotnet test /home/buvy/code/NorseArchitecture/Bifrost/Urdarbrunnr/tests/Persistence.EntityFramework.Design.Tests
```
Expected: build FAILS with `CS1061` (no `AddNorseDesignTimeServices` extension method found on `IServiceCollection`).

- [ ] **Step 13: Implement `NorseDesignTimeServicesExtensions`**

```bash
cat > /home/buvy/code/NorseArchitecture/Bifrost/Urdarbrunnr/src/Persistence.EntityFramework.Design/NorseDesignTimeServicesExtensions.cs << 'EOF'
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations.Design;
using Microsoft.Extensions.DependencyInjection;

namespace Norse.Persistence.EntityFramework.Design;

/// <summary>
/// Installs <see cref="DdlEmittingMigrationsScaffolder"/> as EF's <see cref="IMigrationsScaffolder"/>.
/// A downstream realm's own <c>IDesignTimeServices</c> implementation calls this from its
/// <c>.Migrations.{Provider}</c> project -- the one place EF's tooling actually reflects over to
/// discover design-time services, so this boilerplate can't be hoisted any further up the chassis.
/// </summary>
/// <example>
/// <code>
/// sealed class DesignTimeServices : IDesignTimeServices
/// {
///     public void ConfigureDesignTimeServices(IServiceCollection services) =>
///         services.AddNorseDesignTimeServices("norse_referencedata");
/// }
/// </code>
/// </example>
public static class NorseDesignTimeServicesExtensions
{
	/// <param name="services">The design-time service collection EF's tooling supplies.</param>
	/// <param name="databaseName">
	/// The realm's database name (e.g. <c>"norse_referencedata"</c>) -- used both as this call's
	/// default dev connection-string database and as the emitted schema file's name
	/// (<c>schema/{databaseName}.sql</c>, resolved via <see cref="DesignTimeSchemaPath"/>).
	/// </param>
	/// <returns>The same <paramref name="services"/> for chaining.</returns>
	public static IServiceCollection AddNorseDesignTimeServices(this IServiceCollection services, string databaseName)
	{
		var outputFilePath = DesignTimeSchemaPath.Resolve(AppContext.BaseDirectory, databaseName);
		var efDescriptor = services.LastOrDefault(d => d.ServiceType == typeof(IMigrationsScaffolder));

		return services.AddSingleton<IMigrationsScaffolder>(sp =>
		{
			var inner = efDescriptor switch
			{
				{ ImplementationType: not null } => (IMigrationsScaffolder)ActivatorUtilities.CreateInstance(sp, efDescriptor.ImplementationType),
				{ ImplementationFactory: not null } => (IMigrationsScaffolder)efDescriptor.ImplementationFactory(sp),
				{ ImplementationInstance: not null } => (IMigrationsScaffolder)efDescriptor.ImplementationInstance,
				_ => throw new InvalidOperationException(
					"Could not locate Entity Framework's IMigrationsScaffolder registration. Ensure Microsoft.EntityFrameworkCore.Design is referenced correctly.")
			};
			return new DdlEmittingMigrationsScaffolder(inner, sp.GetRequiredService<ICurrentDbContext>(), outputFilePath);
		});
	}
}
EOF
```

- [ ] **Step 14: Run the tests to verify they pass**

```bash
dotnet test /home/buvy/code/NorseArchitecture/Bifrost/Urdarbrunnr/tests/Persistence.EntityFramework.Design.Tests
```
Expected: PASS — all tests in the project (2 `DesignTimeSchemaPathTests` + 5 `DdlEmittingMigrationsScaffolderTests` + 3 `NorseDesignTimeServicesExtensionsTests` = 10), 0 failed.

- [ ] **Step 15: Build the whole realm to confirm nothing else broke**

```bash
dotnet build /home/buvy/code/NorseArchitecture/Bifrost/Urdarbrunnr/Urdarbrunnr.slnx
dotnet test /home/buvy/code/NorseArchitecture/Bifrost/Urdarbrunnr/Urdarbrunnr.slnx
```
Expected: build succeeds, 0 errors. All tests across the whole realm pass, 0 failed.

- [ ] **Step 16: Commit**

```bash
U=/home/buvy/code/NorseArchitecture/Bifrost/Urdarbrunnr
git -C "$U" add -A
git -C "$U" commit -m "$(cat <<'EOF'
Add DDL-emission to the Design chassis

DdlEmittingMigrationsScaffolder decorates EF's IMigrationsScaffolder: every
migrations add/remove also writes the current-state schema as plain DDL to a
checked-in file, via AddNorseDesignTimeServices(databaseName). Landing spot
resolved from AppContext.BaseDirectory (DesignTimeSchemaPath), independent of
any downstream realm -- proven here with an in-repo Sqlite-backed test double.
EOF
)"
git -C "$U" status --short
```
Expected: commit succeeds; clean working tree.

---

## Task 4: `NorsePostgreSqlDesignTimeDbContextFactory<TContext>`

**Files:**
- Create: `src/Persistence.EntityFramework.Design.PostgreSQL/NorsePostgreSqlDesignTimeDbContextFactory.cs`
- Create: `tests/Persistence.EntityFramework.Design.PostgreSQL.Tests/Persistence.EntityFramework.Design.PostgreSQL.Tests.csproj`
- Create: `tests/Persistence.EntityFramework.Design.PostgreSQL.Tests/NorsePostgreSqlDesignTimeDbContextFactoryTests.cs`
- Modify: `Urdarbrunnr.slnx` (register the new test project)

**Interfaces:**
- Consumes: `Norse.Persistence.EntityFramework.NorseDbContextOptionsExtensions.ApplyNorseConventions(DbContextOptionsBuilder)` (existing), `Microsoft.EntityFrameworkCore.Design.IDesignTimeDbContextFactory<TContext>` (lives in the main `Microsoft.EntityFrameworkCore` package — confirmed by reflection; no extra package reference needed beyond the existing `ProjectReference` to `Persistence.EntityFramework.PostgreSQL`), `Npgsql.EntityFrameworkCore.PostgreSQL`'s `UseNpgsql(string, Action<NpgsqlDbContextOptionsBuilder>)` (already resolves transitively through `Persistence.EntityFramework.PostgreSQL`).
- Produces: `NorsePostgreSqlDesignTimeDbContextFactory<TContext>` (public abstract, `where TContext : DbContext, INorseDbContext`) — members: `protected abstract string DatabaseName { get; }`, `protected virtual string DefaultConnectionString { get; }`, `protected virtual bool UseSnakeCaseNaming => true`, `protected virtual void ConfigureOptions(DbContextOptionsBuilder<TContext> builder, string connectionString)`, `protected abstract TContext CreateContext(DbContextOptions<TContext> options)`, `public TContext CreateDbContext(string[] args)`. Later (deferred) downstream realms derive from this; `ConfigureOptions` is the hook an ASP.NET Identity-style consumer overrides to layer in `UseApplicationServiceProvider`.

- [ ] **Step 1: Create the new test project**

```bash
mkdir -p /home/buvy/code/NorseArchitecture/Bifrost/Urdarbrunnr/tests/Persistence.EntityFramework.Design.PostgreSQL.Tests
cat > /home/buvy/code/NorseArchitecture/Bifrost/Urdarbrunnr/tests/Persistence.EntityFramework.Design.PostgreSQL.Tests/Persistence.EntityFramework.Design.PostgreSQL.Tests.csproj << 'EOF'
<Project Sdk="Microsoft.NET.Sdk">
	<ItemGroup>
		<ProjectReference Include="../../src/Persistence.EntityFramework.Design.PostgreSQL/Persistence.EntityFramework.Design.PostgreSQL.csproj" />
	</ItemGroup>
</Project>
EOF
```

Register it in `Urdarbrunnr.slnx`. Read the current `<Folder Path="/tests/">` section first:

```bash
grep -n "tests/Persistence.EntityFramework.Design" /home/buvy/code/NorseArchitecture/Bifrost/Urdarbrunnr/Urdarbrunnr.slnx
```

Use the Edit tool to add, alongside the existing `Persistence.EntityFramework.Design.Tests` entry:

```xml
		<Project Path="tests/Persistence.EntityFramework.Design.PostgreSQL.Tests/Persistence.EntityFramework.Design.PostgreSQL.Tests.csproj" />
```

- [ ] **Step 2: Write the failing tests**

```bash
cat > /home/buvy/code/NorseArchitecture/Bifrost/Urdarbrunnr/tests/Persistence.EntityFramework.Design.PostgreSQL.Tests/NorsePostgreSqlDesignTimeDbContextFactoryTests.cs << 'EOF'
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Norse.Persistence.EntityFramework.Design.PostgreSQL.Tests;

public sealed class NorsePostgreSqlDesignTimeDbContextFactoryTests
{
	[Fact]
	void CreateDbContext_wires_the_npgsql_provider()
	{
		StubFactory factory = new();

		using var ctx = factory.CreateDbContext([]);

		ctx.Database.ProviderName.ShouldBe("Npgsql.EntityFrameworkCore.PostgreSQL");
	}

	[Fact]
	void CreateDbContext_defaults_to_snake_case_naming()
	{
		StubFactory factory = new();

		using var ctx = factory.CreateDbContext([]);

		var tableName = ctx.Model.FindEntityType(typeof(StubEntity))!.GetTableName();

		tableName.ShouldBe("stub_entities");
	}

	[Fact]
	void CreateDbContext_uses_the_environment_connection_string_override_when_set()
	{
		Environment.SetEnvironmentVariable("DOTNET_EFTOOLS_CONNECTIONSTRING", "Host=override-host;Database=override");
		try
		{
			StubFactory factory = new();

			using var ctx = factory.CreateDbContext([]);

			ctx.Database.GetConnectionString().ShouldBe("Host=override-host;Database=override");
		}
		finally
		{
			Environment.SetEnvironmentVariable("DOTNET_EFTOOLS_CONNECTIONSTRING", null);
		}
	}

	[Fact]
	void A_subclass_overriding_ConfigureOptions_composes_with_the_base_wiring_instead_of_replacing_it()
	{
		OverridingStubFactory factory = new();

		using var ctx = factory.CreateDbContext([]);

		ctx.Database.ProviderName.ShouldBe("Npgsql.EntityFrameworkCore.PostgreSQL");
		factory.ExtraConfigurationRan.ShouldBeTrue();
	}

	sealed class StubContext(DbContextOptions<StubContext> options) : NorseDbContext(options)
	{
		public DbSet<StubEntity> StubEntities => Set<StubEntity>();
	}

	sealed class StubEntity : INorseEntity<StubEntity>
	{
		public int Id { get; set; }

		public static void Configure(EntityTypeBuilder<StubEntity> builder)
		{
		}
	}

	sealed class StubFactory : NorsePostgreSqlDesignTimeDbContextFactory<StubContext>
	{
		protected override string DatabaseName => "stub_db";

		protected override StubContext CreateContext(DbContextOptions<StubContext> options) => new(options);
	}

	sealed class OverridingStubFactory : NorsePostgreSqlDesignTimeDbContextFactory<StubContext>
	{
		public bool ExtraConfigurationRan { get; private set; }

		protected override string DatabaseName => "stub_db";

		protected override void ConfigureOptions(DbContextOptionsBuilder<StubContext> builder, string connectionString)
		{
			base.ConfigureOptions(builder, connectionString);
			ExtraConfigurationRan = true;
		}

		protected override StubContext CreateContext(DbContextOptions<StubContext> options) => new(options);
	}
}
EOF
```

- [ ] **Step 3: Run to verify failure**

```bash
dotnet test /home/buvy/code/NorseArchitecture/Bifrost/Urdarbrunnr/tests/Persistence.EntityFramework.Design.PostgreSQL.Tests
```
Expected: build FAILS — `CS0246: The type or namespace name 'NorsePostgreSqlDesignTimeDbContextFactory' could not be found`.

- [ ] **Step 4: Implement `NorsePostgreSqlDesignTimeDbContextFactory<TContext>`**

```bash
cat > /home/buvy/code/NorseArchitecture/Bifrost/Urdarbrunnr/src/Persistence.EntityFramework.Design.PostgreSQL/NorsePostgreSqlDesignTimeDbContextFactory.cs << 'EOF'
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Norse.Persistence.EntityFramework.Design.PostgreSQL;

/// <summary>
/// Base <see cref="IDesignTimeDbContextFactory{TContext}"/> for Postgres-backed Norse contexts, used
/// only by <c>dotnet ef</c> tooling. Wires the Npgsql provider and Norse's snake_case naming
/// convention (this factory only ever targets Postgres, so applying it unconditionally is correct --
/// unlike the ambiguity <c>NorsePostgresContextExtensions</c>' runtime registration gates behind
/// <c>useSnakeCaseNaming</c>). <see cref="ConfigureOptions"/> is a second, narrower override point
/// than <see cref="CreateContext"/> alone provides -- a subclass whose context needs to configure the
/// <see cref="DbContextOptionsBuilder{TContext}"/> itself before <c>.Options</c> is built (e.g. an
/// ASP.NET Core Identity-style context calling <c>UseApplicationServiceProvider</c> to control schema
/// version) overrides it and calls <c>base.ConfigureOptions(...)</c> rather than reimplementing the
/// provider/connection-string/naming wiring from scratch.
/// </summary>
/// <typeparam name="TContext">The Norse EF context this factory constructs at design time.</typeparam>
public abstract class NorsePostgreSqlDesignTimeDbContextFactory<TContext> : IDesignTimeDbContextFactory<TContext>
	where TContext : DbContext, INorseDbContext
{
	/// <summary>The realm's database name -- e.g. <c>"norse_referencedata"</c>.</summary>
	protected abstract string DatabaseName { get; }

	/// <summary>
	/// The connection string used when <c>DOTNET_EFTOOLS_CONNECTIONSTRING</c> is not set. Points at
	/// the local dev Postgres container by convention.
	/// </summary>
	protected virtual string DefaultConnectionString =>
		$"Host=localhost;Port=5432;Database={DatabaseName};Username=postgres;Password=devpassword";

	/// <summary>
	/// Whether Norse's snake_case naming convention is applied. Defaults to <see langword="true"/>,
	/// matching <c>NorsePostgresContextExtensions</c>' own Postgres default -- override only if the
	/// realm's runtime registration also opts out, to keep design-time scaffolding consistent with
	/// what the running container actually produces.
	/// </summary>
	protected virtual bool UseSnakeCaseNaming => true;

	/// <inheritdoc />
	public TContext CreateDbContext(string[] args)
	{
		var connectionString =
			Environment.GetEnvironmentVariable("DOTNET_EFTOOLS_CONNECTIONSTRING")
			?? DefaultConnectionString;

		DbContextOptionsBuilder<TContext> optionsBuilder = new();
		ConfigureOptions(optionsBuilder, connectionString);

		return CreateContext(optionsBuilder.Options);
	}

	/// <summary>
	/// Configures the options builder -- provider, connection string, and (conditionally) naming
	/// conventions. Override to layer in additional configuration; call <c>base.ConfigureOptions(...)</c>
	/// first unless deliberately replacing the base wiring entirely.
	/// </summary>
	protected virtual void ConfigureOptions(DbContextOptionsBuilder<TContext> builder, string connectionString)
	{
		builder.UseNpgsql(connectionString,
			o => o.MigrationsAssembly(GetType().Assembly.GetName().Name));

		if (UseSnakeCaseNaming)
			NorseDbContextOptionsExtensions.ApplyNorseConventions(builder);
	}

	/// <summary>Constructs <typeparamref name="TContext"/> from the configured options.</summary>
	protected abstract TContext CreateContext(DbContextOptions<TContext> options);
}
EOF
```

- [ ] **Step 5: Run to verify the tests pass**

```bash
dotnet test /home/buvy/code/NorseArchitecture/Bifrost/Urdarbrunnr/tests/Persistence.EntityFramework.Design.PostgreSQL.Tests
```
Expected: PASS, 4 tests, 0 failed.

- [ ] **Step 6: Commit**

```bash
U=/home/buvy/code/NorseArchitecture/Bifrost/Urdarbrunnr
git -C "$U" add -A
git -C "$U" commit -m "$(cat <<'EOF'
Add NorsePostgreSqlDesignTimeDbContextFactory<T>

Base IDesignTimeDbContextFactory for Postgres-backed Norse contexts: wires
Npgsql + snake_case naming by default. Exposes ConfigureOptions as a second,
narrower override point beyond CreateContext(options) alone, so a future
ASP.NET Identity-style consumer can layer in UseApplicationServiceProvider
without bypassing the base -- proven here by a test subclass override.
EOF
)"
git -C "$U" status --short
```
Expected: commit succeeds; clean working tree.

---

## Task 5: `NorseSqlServerDesignTimeDbContextFactory<TContext>`

**Files:**
- Create: `src/Persistence.EntityFramework.Design.SqlServer/NorseSqlServerDesignTimeDbContextFactory.cs`
- Create: `tests/Persistence.EntityFramework.Design.SqlServer.Tests/Persistence.EntityFramework.Design.SqlServer.Tests.csproj`
- Create: `tests/Persistence.EntityFramework.Design.SqlServer.Tests/NorseSqlServerDesignTimeDbContextFactoryTests.cs`
- Modify: `Urdarbrunnr.slnx` (register the new test project)

**Interfaces:**
- Consumes: same as Task 4, mirrored for SQL Server — `Microsoft.EntityFrameworkCore.SqlServer`'s `UseSqlServer(string, Action<SqlServerDbContextOptionsBuilder>)`, already resolving transitively through the existing `ProjectReference` to `Persistence.EntityFramework.SqlServer`.
- Produces: `NorseSqlServerDesignTimeDbContextFactory<TContext>` — identical member shape to Task 4's factory, `UseSnakeCaseNaming` defaulting to `false` (matching `NorseSqlServerContextExtensions`' own runtime default: SQL Server's case-insensitive collation makes raw PascalCase round-trip fine without Postgres's escaping problem).

- [ ] **Step 1: Create the new test project**

```bash
mkdir -p /home/buvy/code/NorseArchitecture/Bifrost/Urdarbrunnr/tests/Persistence.EntityFramework.Design.SqlServer.Tests
cat > /home/buvy/code/NorseArchitecture/Bifrost/Urdarbrunnr/tests/Persistence.EntityFramework.Design.SqlServer.Tests/Persistence.EntityFramework.Design.SqlServer.Tests.csproj << 'EOF'
<Project Sdk="Microsoft.NET.Sdk">
	<ItemGroup>
		<ProjectReference Include="../../src/Persistence.EntityFramework.Design.SqlServer/Persistence.EntityFramework.Design.SqlServer.csproj" />
	</ItemGroup>
</Project>
EOF
```

Use the Edit tool on `Urdarbrunnr.slnx` to add, alongside the Task 4 entry:

```xml
		<Project Path="tests/Persistence.EntityFramework.Design.SqlServer.Tests/Persistence.EntityFramework.Design.SqlServer.Tests.csproj" />
```

- [ ] **Step 2: Write the failing tests**

```bash
cat > /home/buvy/code/NorseArchitecture/Bifrost/Urdarbrunnr/tests/Persistence.EntityFramework.Design.SqlServer.Tests/NorseSqlServerDesignTimeDbContextFactoryTests.cs << 'EOF'
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Norse.Persistence.EntityFramework.Design.SqlServer.Tests;

public sealed class NorseSqlServerDesignTimeDbContextFactoryTests
{
	[Fact]
	void CreateDbContext_wires_the_sql_server_provider()
	{
		StubFactory factory = new();

		using var ctx = factory.CreateDbContext([]);

		ctx.Database.ProviderName.ShouldBe("Microsoft.EntityFrameworkCore.SqlServer");
	}

	[Fact]
	void CreateDbContext_defaults_to_pascal_case_naming()
	{
		StubFactory factory = new();

		using var ctx = factory.CreateDbContext([]);

		var tableName = ctx.Model.FindEntityType(typeof(StubEntity))!.GetTableName();

		tableName.ShouldBe("StubEntities");
	}

	[Fact]
	void CreateDbContext_uses_the_environment_connection_string_override_when_set()
	{
		Environment.SetEnvironmentVariable("DOTNET_EFTOOLS_CONNECTIONSTRING", "Server=override-host;Database=override");
		try
		{
			StubFactory factory = new();

			using var ctx = factory.CreateDbContext([]);

			ctx.Database.GetConnectionString().ShouldBe("Server=override-host;Database=override");
		}
		finally
		{
			Environment.SetEnvironmentVariable("DOTNET_EFTOOLS_CONNECTIONSTRING", null);
		}
	}

	[Fact]
	void A_subclass_overriding_ConfigureOptions_composes_with_the_base_wiring_instead_of_replacing_it()
	{
		OverridingStubFactory factory = new();

		using var ctx = factory.CreateDbContext([]);

		ctx.Database.ProviderName.ShouldBe("Microsoft.EntityFrameworkCore.SqlServer");
		factory.ExtraConfigurationRan.ShouldBeTrue();
	}

	sealed class StubContext(DbContextOptions<StubContext> options) : NorseDbContext(options)
	{
		public DbSet<StubEntity> StubEntities => Set<StubEntity>();
	}

	sealed class StubEntity : INorseEntity<StubEntity>
	{
		public int Id { get; set; }

		public static void Configure(EntityTypeBuilder<StubEntity> builder)
		{
		}
	}

	sealed class StubFactory : NorseSqlServerDesignTimeDbContextFactory<StubContext>
	{
		protected override string DatabaseName => "stub_db";

		protected override StubContext CreateContext(DbContextOptions<StubContext> options) => new(options);
	}

	sealed class OverridingStubFactory : NorseSqlServerDesignTimeDbContextFactory<StubContext>
	{
		public bool ExtraConfigurationRan { get; private set; }

		protected override string DatabaseName => "stub_db";

		protected override void ConfigureOptions(DbContextOptionsBuilder<StubContext> builder, string connectionString)
		{
			base.ConfigureOptions(builder, connectionString);
			ExtraConfigurationRan = true;
		}

		protected override StubContext CreateContext(DbContextOptions<StubContext> options) => new(options);
	}
}
EOF
```

- [ ] **Step 3: Run to verify failure**

```bash
dotnet test /home/buvy/code/NorseArchitecture/Bifrost/Urdarbrunnr/tests/Persistence.EntityFramework.Design.SqlServer.Tests
```
Expected: build FAILS — `CS0246: The type or namespace name 'NorseSqlServerDesignTimeDbContextFactory' could not be found`.

- [ ] **Step 4: Implement `NorseSqlServerDesignTimeDbContextFactory<TContext>`**

```bash
cat > /home/buvy/code/NorseArchitecture/Bifrost/Urdarbrunnr/src/Persistence.EntityFramework.Design.SqlServer/NorseSqlServerDesignTimeDbContextFactory.cs << 'EOF'
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Norse.Persistence.EntityFramework.Design.SqlServer;

/// <summary>
/// Base <see cref="IDesignTimeDbContextFactory{TContext}"/> for SQL Server-backed Norse contexts, used
/// only by <c>dotnet ef</c> tooling. Wires the SQL Server provider; naming stays PascalCase by default
/// (matching <c>NorseSqlServerContextExtensions</c>' own runtime default -- SQL Server's
/// case-insensitive collation round-trips raw PascalCase fine, unlike Postgres). See
/// <see cref="Norse.Persistence.EntityFramework.Design.PostgreSQL.NorsePostgreSqlDesignTimeDbContextFactory{TContext}"/>
/// for the full rationale behind the <see cref="ConfigureOptions"/> extension point.
/// </summary>
/// <typeparam name="TContext">The Norse EF context this factory constructs at design time.</typeparam>
public abstract class NorseSqlServerDesignTimeDbContextFactory<TContext> : IDesignTimeDbContextFactory<TContext>
	where TContext : DbContext, INorseDbContext
{
	/// <summary>The realm's database name -- e.g. <c>"norse_identity"</c>.</summary>
	protected abstract string DatabaseName { get; }

	/// <summary>
	/// The connection string used when <c>DOTNET_EFTOOLS_CONNECTIONSTRING</c> is not set. Points at a
	/// local dev SQL Server container by convention -- provisional until Bifröst wires a real one into
	/// the AppHost (deferred, see the design doc).
	/// </summary>
	protected virtual string DefaultConnectionString =>
		$"Server=localhost;Database={DatabaseName};User Id=sa;Password=devpassword;TrustServerCertificate=true";

	/// <summary>
	/// Whether Norse's snake_case naming convention is applied. Defaults to <see langword="false"/>,
	/// matching <c>NorseSqlServerContextExtensions</c>' own SQL Server default -- override only if the
	/// realm's runtime registration also opts in, to keep design-time scaffolding consistent with what
	/// the running container actually produces.
	/// </summary>
	protected virtual bool UseSnakeCaseNaming => false;

	/// <inheritdoc />
	public TContext CreateDbContext(string[] args)
	{
		var connectionString =
			Environment.GetEnvironmentVariable("DOTNET_EFTOOLS_CONNECTIONSTRING")
			?? DefaultConnectionString;

		DbContextOptionsBuilder<TContext> optionsBuilder = new();
		ConfigureOptions(optionsBuilder, connectionString);

		return CreateContext(optionsBuilder.Options);
	}

	/// <summary>
	/// Configures the options builder -- provider, connection string, and (conditionally) naming
	/// conventions. Override to layer in additional configuration; call <c>base.ConfigureOptions(...)</c>
	/// first unless deliberately replacing the base wiring entirely.
	/// </summary>
	protected virtual void ConfigureOptions(DbContextOptionsBuilder<TContext> builder, string connectionString)
	{
		builder.UseSqlServer(connectionString,
			o => o.MigrationsAssembly(GetType().Assembly.GetName().Name));

		if (UseSnakeCaseNaming)
			NorseDbContextOptionsExtensions.ApplyNorseConventions(builder);
	}

	/// <summary>Constructs <typeparamref name="TContext"/> from the configured options.</summary>
	protected abstract TContext CreateContext(DbContextOptions<TContext> options);
}
EOF
```

- [ ] **Step 5: Run to verify the tests pass**

```bash
dotnet test /home/buvy/code/NorseArchitecture/Bifrost/Urdarbrunnr/tests/Persistence.EntityFramework.Design.SqlServer.Tests
```
Expected: PASS, 4 tests, 0 failed.

- [ ] **Step 6: Build and test the whole realm**

```bash
dotnet build /home/buvy/code/NorseArchitecture/Bifrost/Urdarbrunnr/Urdarbrunnr.slnx
dotnet test /home/buvy/code/NorseArchitecture/Bifrost/Urdarbrunnr/Urdarbrunnr.slnx
```
Expected: build succeeds, 0 errors. All tests across the whole realm pass, 0 failed.

- [ ] **Step 7: Commit**

```bash
U=/home/buvy/code/NorseArchitecture/Bifrost/Urdarbrunnr
git -C "$U" add -A
git -C "$U" commit -m "$(cat <<'EOF'
Add NorseSqlServerDesignTimeDbContextFactory<T>

Mirrors NorsePostgreSqlDesignTimeDbContextFactory<T> for SQL Server: wires the
SqlServer provider, defaults naming to PascalCase (matching
NorseSqlServerContextExtensions' own runtime default), same ConfigureOptions
extension point.
EOF
)"
git -C "$U" status --short
```
Expected: commit succeeds; clean working tree.

---

## Task 6: `SnakeCaseNameRewriter` regression coverage for ASP.NET Identity's built-in index names

**Files:**
- Modify: `tests/Persistence.EntityFramework.Tests/SnakeCaseNameRewriterTests.cs`

**Interfaces:**
- Consumes: `Norse.Persistence.EntityFramework.SnakeCaseNameRewriter.RewriteName(string)` (existing, unchanged).
- Produces: nothing new — this task only adds test coverage for a case the design doc's research proved the existing algorithm already handles correctly, guarding against a regression of a proven historical failure mode.

- [ ] **Step 1: Write the new test cases**

Read the existing file first to match its style exactly:

```bash
cat /home/buvy/code/NorseArchitecture/Bifrost/Urdarbrunnr/tests/Persistence.EntityFramework.Tests/SnakeCaseNameRewriterTests.cs
```

Use the Edit tool to add these three `[Fact]` methods inside the existing `SnakeCaseNameRewriterTests` class, near the other `PascalCase`/`CamelCase` cases:

```csharp
	[Fact]
	void AspNetIdentity_UserNameIndex_splits_at_every_word_boundary()
	{
		SnakeCaseNameRewriter.RewriteName("UserNameIndex").ShouldBe("user_name_index");
	}

	[Fact]
	void AspNetIdentity_EmailIndex_splits_at_the_word_boundary()
	{
		SnakeCaseNameRewriter.RewriteName("EmailIndex").ShouldBe("email_index");
	}

	[Fact]
	void AspNetIdentity_RoleNameIndex_splits_at_every_word_boundary()
	{
		SnakeCaseNameRewriter.RewriteName("RoleNameIndex").ShouldBe("role_name_index");
	}
```

- [ ] **Step 2: Run the tests to verify they pass**

```bash
dotnet test /home/buvy/code/NorseArchitecture/Bifrost/Urdarbrunnr/tests/Persistence.EntityFramework.Tests
```
Expected: PASS — the 3 new cases plus every existing case in the file, 0 failed. (These should pass on the first run without any implementation change — the design doc's manual trace already confirmed `SnakeCaseNameRewriter` handles this correctly; this step is the automated proof.)

If any of the three fail, do not "fix" `SnakeCaseNameRewriter` casually — this is the exact historical bug (a prior rewrite of this algorithm mangled these into `usernameindex`, undelimited). Stop and re-examine the algorithm's word-boundary logic (`SnakeCaseNameRewriter.cs`) against the failing case before changing anything.

- [ ] **Step 3: Build and test the whole realm one final time**

```bash
dotnet build /home/buvy/code/NorseArchitecture/Bifrost/Urdarbrunnr/Urdarbrunnr.slnx
dotnet test /home/buvy/code/NorseArchitecture/Bifrost/Urdarbrunnr/Urdarbrunnr.slnx
```
Expected: build succeeds, 0 errors. All tests across the whole realm pass, 0 failed. This is the plan's final green build.

- [ ] **Step 4: Commit**

```bash
U=/home/buvy/code/NorseArchitecture/Bifrost/Urdarbrunnr
git -C "$U" add -A
git -C "$U" commit -m "$(cat <<'EOF'
Add SnakeCaseNameRewriter regression coverage for ASP.NET Identity index names

UserNameIndex/EmailIndex/RoleNameIndex are ASP.NET Core Identity's own
built-in index names. A prior rewrite of this same algorithm (in different
prior art) mangled these into undelimited lowercase and needed a dedicated
follow-up migration to fix. This is proven-historical, not hypothetical --
cheap insurance, and directly relevant once Himinbjorg's planned hand-cleanup
of its hardcoded index names starts relying on this rewriter for these exact
names.
EOF
)"
git -C "$U" status --short
```
Expected: commit succeeds; clean working tree. This is the plan's last task.

---

## Self-Review Notes

**Spec coverage:** §2's in-scope items are covered — chassis rename (Task 1), `NorseDesignRef`/generator-strip mechanism (already live platform-wide per Bifröst's `Directory.Build.targets`, nothing to build — confirmed in the spec, not re-verified here since it's out of this plan's file-touch scope), DDL emission (Task 3), both factory bases with the `ConfigureOptions` hook (Tasks 4–5). §4's landing-spot/embedding convention is documented for the deferred downstream pass, not implemented here (correctly out of scope). §6's regression test is Task 6. Deferred items (§2, §5) are named in this plan's Global Constraints and left untouched.

**Placeholder scan:** no TBD/TODO; every step shows complete, runnable commands or complete code.

**Type consistency:** `DesignTimeSchemaPath.Resolve(string, string)`, `DdlEmittingMigrationsScaffolder`'s constructor `(IMigrationsScaffolder, ICurrentDbContext, string)`, and `AddNorseDesignTimeServices(this IServiceCollection, string databaseName)` are used identically across Tasks 3–5 wherever referenced. `ConfigureOptions(DbContextOptionsBuilder<TContext>, string)` and `DatabaseName`/`UseSnakeCaseNaming` match between the Postgres and SQL Server factories (Tasks 4–5) by design — same shape, different defaults, as documented.

---

**Plan complete and saved to `docs/superpowers/plans/2026-07-22-design-time-ddl-emission-and-chassis-rename-plan.md`. Two execution options:**

**1. Subagent-Driven (recommended)** - I dispatch a fresh subagent per task, review between tasks, fast iteration

**2. Inline Execution** - Execute tasks in this session using executing-plans, batch execution with checkpoints

**Which approach?**
