# Tabular Ingestion and Seed Tooling Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship `Norse.Primitives.Ingestion` (Svartalfheim) — a canonical forward-only `ITabularReader` contract over Sep and Sylvan.Data.Excel — then Mimisbrunnr's dev-only `tools/SeedTool` console app, which consumes it to convert `seeds/raw/UNSD — Methodology.csv` into `seeds/region.tsv` and `seeds/country-or-area.tsv`.

**Architecture:** Two realms, shipped in dependency order. Svartalfheim gains a new sibling project to `Primitives` with zero dependency on it either way — `Ingestion` only deals in raw `ReadOnlySpan<char>` cells; turning a span into a typed value via `Result<T>`/`Parser` is composed by the caller. Two backends (`SepTabularReader`, `ExcelTabularReader`) satisfy one interface. Mimisbrunnr's `tools/SeedTool` is a new top-level folder (sibling to `src/`/`tests/`), consuming both `Norse.Primitives` and `Norse.Primitives.Ingestion` across the Bifrost `NorseRef` mechanism, to run `UnsdM49Mapper` against the real source file.

**Tech Stack:** .NET 11 preview (net11.0), C# preview, xUnit v3 + Shouldly on Microsoft.Testing.Platform, Sep (`nietras.SeparatedValues` namespace, package id `Sep`), Sylvan.Data.Excel, Spectre.Console.Cli.

## Global Constraints

- **`Ingestion` has zero dependency on `Primitives`, in either direction.** No `ProjectReference`/`NorseRef` between them. Structural failures (malformed delimited row, corrupt xlsx) **throw** from inside `Ingestion` — they are not `Result<T>`. Scalar-value failures are the caller's concern via the existing `Parser.ParseRequired<T>`/`ParseOptional<T>` gateway.
- **`ITabularReader` is single-source, single-sheet, forward-only only.** No multi-sheet navigation, no seeking backward.
- **`SeedTool` is dev-only: never packed, never AOT-published.** `IsPackable=false` and `IsAotCompatible=false` on everything under `tools/`. Confirmed against Yggdrasil's `Hosting.Migrations.Service.csproj` (no `PublishAot`) — it is not part of any runtime path.
- **`Norse.Primitives.Ingestion` itself IS held to the AOT-clean bar**, verified by extending the existing `tests/smoke/Primitives.Aot.Smoke` discipline with a sibling `Primitives.Ingestion.Aot.Smoke` project (`PublishAot=true`, zero warnings, exit 0 required) — not a Glitnir POC.
- **TSV output uses a plain `StreamWriter`, not Sep's writer** — a deliberate refinement made during planning (the design doc's data-flow sketch said "Sep writer" informally). Sep's zero-allocation value is on the read side for untrusted/large external input; writing ~250 already-validated, internally-produced rows doesn't need it, and this avoids committing to an unverified writer-API surface. TSV reading (of the two curated files, once a future seed contributor consumes them) still belongs to Sep — out of scope here.
- **Fail-loud, no partial output:** `UnsdM49Mapper.Map` throws `InvalidOperationException` naming the row number, column name, and raw value on the first bad cell. `SeedTool`'s `Execute` never catches it — if any row fails, neither TSV file is written.
- **Style:** tabs; `var` for return assignments only, explicit type + `new()`/array literal for construction; `sealed` by default; accessibility modifiers omitted when default (test methods bare `void`/`async Task`, no `public`); US English spelling. `Ingestion`'s public API carries XML docs (`GenerateDocumentationFile=true`, inherited from Svartalfheim's root props); `SeedTool` is an internal console tool with no public surface, so its own `Directory.Build.props` turns doc generation off.
- **Ship-gate discipline:** Svartalfheim's tasks (1–4) are reviewable/mergeable/taggable independently before Mimisbrunnr's tasks (5–8) begin. Local development throughout uses `UseProjectReferences=true` (the Bifrost default) via the existing `NorseRef` mechanism — no waiting on a real NuGet publish for any step in this plan.
- **Exact package/namespace facts, verified before this plan was written:** Sep — package id `Sep`, namespace `nietras.SeparatedValues`, confirmed trimmable/AOT-compatible (`IsTrimmable=true`). Sylvan.Data.Excel — package id `Sylvan.Data.Excel`, namespace `Sylvan.Data.Excel`, purely managed with no external dependencies, no explicit AOT claim (hence the smoke test in Task 3).

---

### Task 1: Svartalfheim — `ITabularReader` + `SepTabularReader`

**Files:**
- Create: `Svartalfheim/src/Primitives.Ingestion/Primitives.Ingestion.csproj`
- Create: `Svartalfheim/src/Primitives.Ingestion/ITabularReader.cs`
- Create: `Svartalfheim/src/Primitives.Ingestion/SepTabularReader.cs`
- Create: `Svartalfheim/tests/Primitives.Ingestion.Tests/Primitives.Ingestion.Tests.csproj`
- Create: `Svartalfheim/tests/Primitives.Ingestion.Tests/SepTabularReaderTests.cs`
- Modify: `Svartalfheim/Svartalfheim.slnx`

**Interfaces:**
- Produces: `Norse.Primitives.Ingestion.ITabularReader` — `int FieldCount { get; }`, `int Ordinal(string headerName)`, `bool Read()`, `ReadOnlySpan<char> this[int ordinal] { get; }`, `IDisposable`. `Norse.Primitives.Ingestion.SepTabularReader` (`internal sealed`, constructed `new SepTabularReader(string path, char separator)`) — this is what Task 2's `ExcelTabularReader` matches the shape of, and what Task 6's `UnsdM49Mapper` and Task 3's smoke test both construct directly.

- [ ] **Step 1: Scaffold the source project**

```xml
<!-- Svartalfheim/src/Primitives.Ingestion/Primitives.Ingestion.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
	<PropertyGroup>
		<Description>Norse forward-only tabular ingestion: a canonical ITabularReader contract over Sep (delimited) and Sylvan.Data.Excel (single-sheet Excel), for turning untrusted source files into cell spans. Scalar-value conversion of those spans into typed values is Norse.Primitives' job, composed by the caller — this project carries no dependency on it.</Description>
	</PropertyGroup>
	<ItemGroup>
		<PackageReference Include="Sep" Version="*" />
	</ItemGroup>
</Project>
```

```csharp
// Svartalfheim/src/Primitives.Ingestion/ITabularReader.cs
namespace Norse.Primitives.Ingestion;

/// <summary>
/// A forward-only, single-source, single-sheet cursor over tabular data (a delimited file
/// or a single Excel worksheet), surfacing every cell as a <see cref="ReadOnlySpan{Char}"/>
/// regardless of the underlying format.
/// </summary>
/// <remarks>
/// Structural failures (a malformed delimited row, a corrupt workbook) are this contract's
/// own concern and throw — they are not <c>Result&lt;T&gt;</c> territory. Turning a cell's
/// span into a typed scalar value, and deciding what a bad value means, belongs to the
/// caller via <c>Norse.Primitives.Parser</c>.
/// </remarks>
public interface ITabularReader : IDisposable
{
	/// <summary>The number of columns, resolved from the header row.</summary>
	int FieldCount { get; }

	/// <summary>Resolves a column's ordinal from its header name once, for reuse in a hot read loop.</summary>
	/// <param name="headerName">The header name to look up.</param>
	/// <returns>The zero-based column ordinal.</returns>
	int Ordinal(string headerName);

	/// <summary>Advances to the next row.</summary>
	/// <returns><see langword="false"/> when there are no more rows.</returns>
	bool Read();

	/// <summary>The current row's cell at <paramref name="ordinal"/>, as raw text.</summary>
	/// <param name="ordinal">The zero-based column ordinal.</param>
	ReadOnlySpan<char> this[int ordinal] { get; }
}
```

```csharp
// Svartalfheim/src/Primitives.Ingestion/SepTabularReader.cs
using nietras.SeparatedValues;

namespace Norse.Primitives.Ingestion;

/// <summary>An <see cref="ITabularReader"/> over a delimited file, backed by Sep.</summary>
sealed class SepTabularReader : ITabularReader
{
	readonly SepReader _reader;

	/// <summary>Opens <paramref name="path"/> for forward-only reading.</summary>
	/// <param name="path">The delimited file's path.</param>
	/// <param name="separator">The field separator (e.g. <c>','</c> for CSV, <c>'\t'</c> for TSV).</param>
	public SepTabularReader(string path, char separator)
	{
		_reader = Sep.New(separator).Reader().FromFile(path);
	}

	public int FieldCount =>
		_reader.Header.ColNames.Count;

	public int Ordinal(string headerName) =>
		_reader.Header.IndexOf(headerName);

	public bool Read() =>
		_reader.MoveNext();

	public ReadOnlySpan<char> this[int ordinal] =>
		_reader.Current[ordinal].Span;

	public void Dispose() =>
		_reader.Dispose();
}
```

- [ ] **Step 2: Scaffold the test project and add both projects to the solution**

```xml
<!-- Svartalfheim/tests/Primitives.Ingestion.Tests/Primitives.Ingestion.Tests.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
	<ItemGroup>
		<ProjectReference Include="..\..\src\Primitives.Ingestion\Primitives.Ingestion.csproj" />
	</ItemGroup>
</Project>
```

Modify `Svartalfheim/Svartalfheim.slnx` to the following full content:

```xml
<Solution>
	<Folder Name="/Solution Items/">
		<File Path=".editorconfig" />
		<File Path=".gitattributes" />
		<File Path=".gitignore" />
		<File Path="Directory.Build.props" />
		<File Path="global.json" />
		<File Path="LICENSE" />
	</Folder>
	<Folder Name="/benchmarks/">
		<File Path="benchmarks/Directory.Build.props" />
		<Project Path="benchmarks/Primitives.Benchmarks/Primitives.Benchmarks.csproj" />
	</Folder>
	<Folder Name="/src/">
		<Project Path="src/Primitives/Primitives.csproj" />
		<Project Path="src/Primitives.Ingestion/Primitives.Ingestion.csproj" />
	</Folder>
	<Folder Name="/tests/">
		<File Path="tests/Directory.Build.props" />
		<File Path="tests/smoke/Directory.Build.props" />
		<Project Path="tests/Primitives.Tests/Primitives.Tests.csproj" />
		<Project Path="tests/Primitives.Ingestion.Tests/Primitives.Ingestion.Tests.csproj" />
		<Project Path="tests/smoke/Primitives.Aot.Smoke/Primitives.Aot.Smoke.csproj" />
		<Project Path="tests/smoke/Primitives.Ingestion.Aot.Smoke/Primitives.Ingestion.Aot.Smoke.csproj" />
	</Folder>
</Solution>
```

(The last `<Project>` line — the new smoke project — doesn't exist on disk yet; it's created in Task 3. Adding it to the `.slnx` now is harmless; `dotnet build Svartalfheim.slnx` in this task only builds the two projects that exist so far because you'll pass explicit project paths below, not the whole solution, until Task 4.)

- [ ] **Step 3: Write the failing tests**

```csharp
// Svartalfheim/tests/Primitives.Ingestion.Tests/SepTabularReaderTests.cs
namespace Norse.Primitives.Ingestion.Tests;

public sealed class SepTabularReaderTests
{
	[Fact]
	void Read_exposes_cells_by_ordinal_and_by_name()
	{
		var path = WriteTempFile("Name,Code\nNigeria,566\nAlgeria,012\n");
		try
		{
			using ITabularReader reader = new SepTabularReader(path, ',');

			reader.FieldCount.ShouldBe(2);
			reader.Read().ShouldBeTrue();
			reader[reader.Ordinal("Name")].ToString().ShouldBe("Nigeria");
			reader[0].ToString().ShouldBe("Nigeria");
			reader[1].ToString().ShouldBe("566");

			reader.Read().ShouldBeTrue();
			reader[reader.Ordinal("Code")].ToString().ShouldBe("012");

			reader.Read().ShouldBeFalse();
		}
		finally
		{
			File.Delete(path);
		}
	}

	[Fact]
	void Read_honors_a_custom_separator()
	{
		var path = WriteTempFile("Name\tCode\nNigeria\t566\n");
		try
		{
			using ITabularReader reader = new SepTabularReader(path, '\t');

			reader.Read().ShouldBeTrue();
			reader[reader.Ordinal("Code")].ToString().ShouldBe("566");
		}
		finally
		{
			File.Delete(path);
		}
	}

	[Fact]
	void Read_throws_on_a_structurally_malformed_row()
	{
		var path = WriteTempFile("Name,Code\nNigeria,566,extra\n");
		try
		{
			using ITabularReader reader = new SepTabularReader(path, ',');

			Should.Throw<Exception>(() => reader.Read());
		}
		finally
		{
			File.Delete(path);
		}
	}

	static string WriteTempFile(string content)
	{
		var path = Path.GetTempFileName();
		File.WriteAllText(path, content);
		return path;
	}
}
```

- [ ] **Step 4: Run the tests to verify they fail**

Run: `dotnet test Svartalfheim/tests/Primitives.Ingestion.Tests/`
Expected: FAIL — `SepTabularReader` does not exist yet if Step 1's file wasn't saved, or the project doesn't resolve `Sep`/`nietras.SeparatedValues` until `dotnet restore` pulls the package. Run `dotnet restore Svartalfheim/src/Primitives.Ingestion/` first if restore errors appear.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test Svartalfheim/tests/Primitives.Ingestion.Tests/`
Expected: PASS (3 tests)

- [ ] **Step 6: Commit**

```bash
git -C Svartalfheim add src/Primitives.Ingestion tests/Primitives.Ingestion.Tests Svartalfheim.slnx
git -C Svartalfheim commit -m "feat: add ITabularReader and SepTabularReader"
```

---

### Task 2: Svartalfheim — `ExcelTabularReader`

**Files:**
- Modify: `Svartalfheim/src/Primitives.Ingestion/Primitives.Ingestion.csproj`
- Create: `Svartalfheim/src/Primitives.Ingestion/ExcelTabularReader.cs`
- Create: `Svartalfheim/tests/Primitives.Ingestion.Tests/ExcelTabularReaderTests.cs`

**Interfaces:**
- Consumes: `Norse.Primitives.Ingestion.ITabularReader` (Task 1).
- Produces: `Norse.Primitives.Ingestion.ExcelTabularReader` (`internal sealed`, constructed `new ExcelTabularReader(string path)`) — same public shape as `SepTabularReader`, proven identical by both satisfying `ITabularReader`.

- [ ] **Step 1: Add the Sylvan.Data.Excel package reference**

```xml
<!-- Svartalfheim/src/Primitives.Ingestion/Primitives.Ingestion.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
	<PropertyGroup>
		<Description>Norse forward-only tabular ingestion: a canonical ITabularReader contract over Sep (delimited) and Sylvan.Data.Excel (single-sheet Excel), for turning untrusted source files into cell spans. Scalar-value conversion of those spans into typed values is Norse.Primitives' job, composed by the caller — this project carries no dependency on it.</Description>
	</PropertyGroup>
	<ItemGroup>
		<PackageReference Include="Sep" Version="*" />
		<PackageReference Include="Sylvan.Data.Excel" Version="*" />
	</ItemGroup>
</Project>
```

- [ ] **Step 2: Write the failing tests**

```csharp
// Svartalfheim/tests/Primitives.Ingestion.Tests/ExcelTabularReaderTests.cs
using System.Data;
using Sylvan.Data.Excel;

namespace Norse.Primitives.Ingestion.Tests;

public sealed class ExcelTabularReaderTests
{
	[Fact]
	void Read_exposes_cells_by_ordinal_and_by_name()
	{
		var path = WriteTempWorkbook();
		try
		{
			using ITabularReader reader = new ExcelTabularReader(path);

			reader.FieldCount.ShouldBe(2);
			reader.Read().ShouldBeTrue();
			reader[reader.Ordinal("Name")].ToString().ShouldBe("Nigeria");
			reader[reader.Ordinal("Code")].ToString().ShouldBe("566");

			reader.Read().ShouldBeTrue();
			reader[reader.Ordinal("Name")].ToString().ShouldBe("Algeria");

			reader.Read().ShouldBeFalse();
		}
		finally
		{
			File.Delete(path);
		}
	}

	[Fact]
	void Read_throws_on_a_corrupt_workbook()
	{
		var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.xlsx");
		File.WriteAllText(path, "this is not a real xlsx file");
		try
		{
			Should.Throw<Exception>(() =>
			{
				using ITabularReader reader = new ExcelTabularReader(path);
				reader.Read();
			});
		}
		finally
		{
			File.Delete(path);
		}
	}

	static string WriteTempWorkbook()
	{
		var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.xlsx");
		DataTable table = new();
		table.Columns.Add("Name", typeof(string));
		table.Columns.Add("Code", typeof(string));
		table.Rows.Add("Nigeria", "566");
		table.Rows.Add("Algeria", "012");

		using var writer = ExcelDataWriter.Create(path);
		writer.Write(table.CreateDataReader(), "Sheet1");

		return path;
	}
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test Svartalfheim/tests/Primitives.Ingestion.Tests/`
Expected: FAIL — `ExcelTabularReader` does not exist (CS0246).

- [ ] **Step 4: Write `ExcelTabularReader`**

```csharp
// Svartalfheim/src/Primitives.Ingestion/ExcelTabularReader.cs
using System.Globalization;
using Sylvan.Data.Excel;

namespace Norse.Primitives.Ingestion;

/// <summary>An <see cref="ITabularReader"/> over a single Excel worksheet, backed by Sylvan.Data.Excel.</summary>
/// <remarks>
/// Unlike <see cref="SepTabularReader"/>, cell access here is not zero-allocation: Excel
/// stores cells as typed values (numeric, date, boolean, text), not as slices of a flat
/// character stream, so each cell's text is materialized as a <see cref="string"/> before
/// this type exposes it as a span. This is a documented asymmetry, not a defect.
/// </remarks>
sealed class ExcelTabularReader : ITabularReader
{
	readonly ExcelDataReader _reader;

	/// <summary>Opens the first worksheet of <paramref name="path"/> for forward-only reading.</summary>
	/// <param name="path">The workbook's path (.xlsx, .xlsb, or .xls).</param>
	public ExcelTabularReader(string path)
	{
		_reader = ExcelDataReader.Create(path);
	}

	public int FieldCount =>
		_reader.FieldCount;

	public int Ordinal(string headerName) =>
		_reader.GetOrdinal(headerName);

	public bool Read() =>
		_reader.Read();

	public ReadOnlySpan<char> this[int ordinal]
	{
		get
		{
			if (_reader.IsDBNull(ordinal))
				return ReadOnlySpan<char>.Empty;
			var value = _reader.GetValue(ordinal);
			var text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
			return text.AsSpan();
		}
	}

	public void Dispose() =>
		_reader.Dispose();
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test Svartalfheim/tests/Primitives.Ingestion.Tests/`
Expected: PASS (5 tests total: 3 from Task 1, 2 new)

- [ ] **Step 6: Commit**

```bash
git -C Svartalfheim add src/Primitives.Ingestion tests/Primitives.Ingestion.Tests
git -C Svartalfheim commit -m "feat: add ExcelTabularReader"
```

---

### Task 3: Svartalfheim — `Primitives.Ingestion.Aot.Smoke`

**Files:**
- Create: `Svartalfheim/tests/smoke/Primitives.Ingestion.Aot.Smoke/Primitives.Ingestion.Aot.Smoke.csproj`
- Create: `Svartalfheim/tests/smoke/Primitives.Ingestion.Aot.Smoke/Program.cs`

**Interfaces:**
- Consumes: `SepTabularReader` (Task 1), `ExcelTabularReader` (Task 2) — constructed directly, exercised through `ITabularReader`.
- Produces: nothing new; this task only proves the existing surface publishes clean under Native AOT.

- [ ] **Step 1: Scaffold the smoke project**

```xml
<!-- Svartalfheim/tests/smoke/Primitives.Ingestion.Aot.Smoke/Primitives.Ingestion.Aot.Smoke.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
	<PropertyGroup>
		<OutputType>Exe</OutputType>
		<PublishAot>true</PublishAot>
	</PropertyGroup>
	<ItemGroup>
		<ProjectReference Include="..\..\..\src\Primitives.Ingestion\Primitives.Ingestion.csproj" />
	</ItemGroup>
</Project>
```

- [ ] **Step 2: Write the smoke program**

```csharp
// Svartalfheim/tests/smoke/Primitives.Ingestion.Aot.Smoke/Program.cs
using System.Data;
using Norse.Primitives.Ingestion;
using Sylvan.Data.Excel;

var failures = 0;
var tempDir = Directory.CreateTempSubdirectory("norse-ingestion-smoke");
try
{
	var csvPath = Path.Combine(tempDir.FullName, "smoke.csv");
	File.WriteAllText(csvPath, "Name,Code\nNigeria,566\nAlgeria,012\n");

	Check("SepTabularReader reads a delimited row by name and by ordinal", () =>
	{
		using ITabularReader reader = new SepTabularReader(csvPath, ',');
		return reader.Read()
			&& reader[reader.Ordinal("Name")].SequenceEqual("Nigeria")
			&& reader[0].SequenceEqual("Nigeria")
			&& reader.Read()
			&& reader[reader.Ordinal("Code")].SequenceEqual("012")
			&& !reader.Read();
	});

	var xlsxPath = Path.Combine(tempDir.FullName, "smoke.xlsx");
	DataTable table = new();
	table.Columns.Add("Name", typeof(string));
	table.Columns.Add("Code", typeof(string));
	table.Rows.Add("Nigeria", "566");
	table.Rows.Add("Algeria", "012");
	using (var excelWriter = ExcelDataWriter.Create(xlsxPath))
		excelWriter.Write(table.CreateDataReader(), "Sheet1");

	Check("ExcelTabularReader reads a single sheet forward-only", () =>
	{
		using ITabularReader reader = new ExcelTabularReader(xlsxPath);
		return reader.Read()
			&& reader[reader.Ordinal("Name")].SequenceEqual("Nigeria")
			&& reader.Read()
			&& reader[reader.Ordinal("Code")].SequenceEqual("012")
			&& !reader.Read();
	});
}
finally
{
	tempDir.Delete(recursive: true);
}

if (failures > 0)
{
	Console.Error.WriteLine($"AOT smoke FAILED: {failures} check(s) failed.");
	return 1;
}

Console.WriteLine("AOT smoke passed: Sep and Sylvan.Data.Excel survive native compilation.");
return 0;

void Check(string description, Func<bool> probe)
{
	bool passed;
	try
	{
		passed = probe();
	}
	catch (Exception exception)
	{
		Console.Error.WriteLine($"FAIL {description}: {exception}");
		failures++;
		return;
	}
	if (passed)
	{
		Console.WriteLine($"ok   {description}");
	}
	else
	{
		Console.Error.WriteLine($"FAIL {description}");
		failures++;
	}
}
```

- [ ] **Step 3: Publish under Native AOT and run it**

Run: `dotnet publish Svartalfheim/tests/smoke/Primitives.Ingestion.Aot.Smoke -c Release`
Expected: succeeds, zero AOT/trim warnings. If Sylvan.Data.Excel produces warnings here, that is this task's actual finding — capture the exact warning text in the commit message and in a follow-up note under this plan's Task 4, rather than suppressing it silently.

Run the published native executable (path printed at the end of `dotnet publish`'s output, under `bin/Release/net11.0/<rid>/publish/`).
Expected: prints both `ok` lines and `AOT smoke passed: ...`, exits 0.

- [ ] **Step 4: Commit**

```bash
git -C Svartalfheim add tests/smoke/Primitives.Ingestion.Aot.Smoke Svartalfheim.slnx
git -C Svartalfheim commit -m "test: add Primitives.Ingestion.Aot.Smoke, proving Sep and Sylvan.Data.Excel under Native AOT"
```

---

### Task 4: Svartalfheim — full-solution verification

**Files:** None (verification-only task; no code changes).

- [ ] **Step 1: Build and test the whole solution**

Run: `dotnet build Svartalfheim/Svartalfheim.slnx`
Expected: succeeds, zero warnings (`TreatWarningsAsErrors=true` platform-wide).

Run: `dotnet test Svartalfheim/tests/Primitives.Tests/ Svartalfheim/tests/Primitives.Ingestion.Tests/`
Expected: all tests pass (existing `Primitives.Tests` unaffected; `Primitives.Ingestion.Tests` from Tasks 1–2 pass).

- [ ] **Step 2: Re-confirm the existing AOT smoke test is undisturbed**

Run: `dotnet publish Svartalfheim/tests/smoke/Primitives.Aot.Smoke -c Release` and run the resulting binary.
Expected: unchanged behavior from before this plan — `Primitives.Ingestion` shares no dependency with `Primitives`, so this smoke test's output is identical to its pre-plan baseline.

- [ ] **Step 3: Report status**

If Steps 1–2 pass, Svartalfheim's half of this plan is done and reviewable now — proceed to Task 5. If Sylvan.Data.Excel's AOT warnings from Task 3 Step 3 turned out to be real (non-zero), note them here as a known limitation rather than blocking: `ExcelTabularReader` still functions under Native AOT for `Read()`-and-typed-getter usage even if trim analysis complains about paths this contract doesn't exercise (e.g. formula evaluation) — confirm by re-running Task 3's smoke binary, which is the actual behavioral proof, not the analyzer warning.

---

### Task 5: Mimisbrunnr — bootstrap `tools/SeedTool` and `Mimisbrunnr.slnx`

**Files:**
- Create: `Mimisbrunnr/tools/Directory.Build.props`
- Create: `Mimisbrunnr/tools/Directory.Build.targets`
- Create: `Mimisbrunnr/tools/SeedTool/SeedTool.csproj`
- Create: `Mimisbrunnr/tools/SeedTool/Program.cs`
- Create: `Mimisbrunnr/tests/SeedTool.Tests/SeedTool.Tests.csproj`
- Create: `Mimisbrunnr/tests/SeedTool.Tests/PlaceholderTests.cs`
- Create: `Mimisbrunnr/Mimisbrunnr.slnx`

**Interfaces:**
- Consumes: `Norse.Primitives.Ingestion.ITabularReader`/`SepTabularReader` (Tasks 1–2, cross-repo via `NorseRef`).
- Produces: an empty-but-building `SeedTool` project and `SeedTool.Tests` project that Tasks 6–7 add real content to.

This is Mimisbrunnr's first code — its `CLAUDE.md` calls it "currently a bare shell." `tools/` is a new top-level folder (sibling to the existing scatter-managed `src/`/`tests/` — this task creates its own `Directory.Build.props`/`.targets`, mirroring the pattern those use, without touching either scattered file).

- [ ] **Step 1: Write `tools/Directory.Build.props`**

```xml
<!-- Mimisbrunnr/tools/Directory.Build.props -->
<Project>
	<PropertyGroup>
		<AnalysisLevel>latest-Recommended</AnalysisLevel>
		<AnalysisLevelSecurity>latest-All</AnalysisLevelSecurity>
		<AnalysisLevelPerformance>latest-All</AnalysisLevelPerformance>
		<AnalysisLevelReliability>latest-All</AnalysisLevelReliability>
		<AnalysisLevelUsage>latest-All</AnalysisLevelUsage>
		<AssemblyName>Norse.$(MSBuildProjectName)</AssemblyName>
		<EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
		<!-- No public API surface here (console tool) — doc generation would only add noise. -->
		<GenerateDocumentationFile>false</GenerateDocumentationFile>
		<ImplicitUsings>enable</ImplicitUsings>
		<LangVersion>preview</LangVersion>
		<Nullable>enable</Nullable>
		<RootNamespace>Norse.$(MSBuildProjectName)</RootNamespace>
		<TargetFramework>net11.0</TargetFramework>
		<TreatWarningsAsErrors>true</TreatWarningsAsErrors>
		<UseProjectReferences Condition="'$(UseProjectReferences)' == ''">false</UseProjectReferences>
		<WarningLevel>9999</WarningLevel>
		<_ParentProps>$([MSBuild]::GetPathOfFileAbove('Directory.Build.props', '$(MSBuildThisFileDirectory)../'))</_ParentProps>
	</PropertyGroup>
	<Import Project="$(_ParentProps)" Condition="Exists('$(_ParentProps)')" />
	<PropertyGroup>
		<!--
			Overrides after the parent import: everything under tools/ is a dev-only console
			utility — never packed, never AOT-published (confirmed against Yggdrasil's
			Hosting.Migrations.Service, which runs as a plain worker container, not Native AOT).
		-->
		<IsAotCompatible>false</IsAotCompatible>
		<IsPackable>false</IsPackable>
	</PropertyGroup>
</Project>
```

- [ ] **Step 2: Write `tools/Directory.Build.targets`**

```xml
<!-- Mimisbrunnr/tools/Directory.Build.targets -->
<Project>
	<PropertyGroup>
		<OutputType>Exe</OutputType>
		<_BifrostTargets>
			$([MSBuild]::GetPathOfFileAbove('Directory.Build.targets', '$(MSBuildThisFileDirectory)../../'))
		</_BifrostTargets>
	</PropertyGroup>
	<Import Project="$(_BifrostTargets)" Condition="Exists('$(_BifrostTargets)')" />
	<ItemGroup Condition="!Exists('$(_BifrostTargets)')">
		<PackageReference Include="@(NorseRef->'Norse.%(Identity)')" Version="*" />
		<PackageReference Include="@(NorseDesignRef->'Norse.%(Identity)')" Version="*">
			<PrivateAssets>all</PrivateAssets>
		</PackageReference>
	</ItemGroup>
	<Target Name="_NorseRemoveUnwantedGeneratorAnalyzers" BeforeTargets="CoreCompile" Condition="'@(Analyzer)' != ''">
		<ItemGroup>
			<_NorseWantedGeneratorAnalyzer Include="@(NorseRef->WithMetadataValue('Generator', 'true')->'Norse.%(Identity).Generator')" />
		</ItemGroup>
		<PropertyGroup>
			<_NorseWantedGeneratorAnalyzerNames>;@(_NorseWantedGeneratorAnalyzer);</_NorseWantedGeneratorAnalyzerNames>
		</PropertyGroup>
		<ItemGroup>
			<Analyzer Remove="@(Analyzer)" Condition="$([System.Text.RegularExpressions.Regex]::IsMatch('%(Analyzer.Filename)', '^Norse\..+\.Generator$')) and !$(_NorseWantedGeneratorAnalyzerNames.Contains(';%(Analyzer.Filename);'))" />
		</ItemGroup>
	</Target>
</Project>
```

- [ ] **Step 3: Scaffold `SeedTool` with a placeholder `Program.cs`**

```xml
<!-- Mimisbrunnr/tools/SeedTool/SeedTool.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
	<ItemGroup>
		<PackageReference Include="Spectre.Console.Cli" Version="*" />
	</ItemGroup>
	<ItemGroup>
		<NorseRef Include="Primitives">
			<Repo>Svartalfheim</Repo>
		</NorseRef>
		<NorseRef Include="Primitives.Ingestion">
			<Repo>Svartalfheim</Repo>
		</NorseRef>
	</ItemGroup>
</Project>
```

```csharp
// Mimisbrunnr/tools/SeedTool/Program.cs
Console.WriteLine("SeedTool scaffold OK.");
return 0;
```

- [ ] **Step 4: Scaffold `SeedTool.Tests` with one trivial passing test**

```xml
<!-- Mimisbrunnr/tests/SeedTool.Tests/SeedTool.Tests.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
	<ItemGroup>
		<ProjectReference Include="..\..\tools\SeedTool\SeedTool.csproj" />
	</ItemGroup>
</Project>
```

```csharp
// Mimisbrunnr/tests/SeedTool.Tests/PlaceholderTests.cs
namespace Norse.SeedTool.Tests;

public sealed class PlaceholderTests
{
	[Fact]
	void Project_wiring_resolves()
	{
		true.ShouldBeTrue();
	}
}
```

(This file is deleted in Task 6 once `UnsdM49MapperTests` gives the test project real content — xUnit v3 fails a run with zero tests, so a placeholder is required until then.)

- [ ] **Step 5: Create `Mimisbrunnr.slnx`**

```xml
<!-- Mimisbrunnr/Mimisbrunnr.slnx -->
<Solution>
	<Folder Name="/Solution Items/">
		<File Path=".editorconfig" />
		<File Path=".gitattributes" />
		<File Path=".gitignore" />
		<File Path="Directory.Build.props" />
		<File Path="global.json" />
		<File Path="LICENSE" />
		<File Path="nuget.config" />
	</Folder>
	<Folder Name="/tools/">
		<File Path="tools/Directory.Build.props" />
		<File Path="tools/Directory.Build.targets" />
		<Project Path="tools/SeedTool/SeedTool.csproj" />
	</Folder>
	<Folder Name="/tests/">
		<File Path="tests/Directory.Build.props" />
		<Project Path="tests/SeedTool.Tests/SeedTool.Tests.csproj" />
	</Folder>
</Solution>
```

- [ ] **Step 6: Build and test the scaffold**

Run: `dotnet build Mimisbrunnr/Mimisbrunnr.slnx -p:UseProjectReferences=true`
Expected: succeeds — `NorseRef` resolves `Primitives`/`Primitives.Ingestion` as `ProjectReference`s into the sibling `Svartalfheim/src/...` checkouts under Bifrost.

Run: `dotnet test Mimisbrunnr/tests/SeedTool.Tests/ -p:UseProjectReferences=true`
Expected: PASS (1 placeholder test).

- [ ] **Step 7: Commit**

```bash
git -C Mimisbrunnr add tools tests/SeedTool.Tests Mimisbrunnr.slnx
git -C Mimisbrunnr commit -m "feat: bootstrap Mimisbrunnr.slnx and the SeedTool console app scaffold"
```

---

### Task 6: Mimisbrunnr — `UnsdM49Mapper`

**Files:**
- Create: `Mimisbrunnr/tools/SeedTool/Mappers/RegionRow.cs`
- Create: `Mimisbrunnr/tools/SeedTool/Mappers/CountryOrAreaRow.cs`
- Create: `Mimisbrunnr/tools/SeedTool/Mappers/UnsdM49Mapper.cs`
- Create: `Mimisbrunnr/tests/SeedTool.Tests/UnsdM49MapperTests.cs`
- Delete: `Mimisbrunnr/tests/SeedTool.Tests/PlaceholderTests.cs`

**Interfaces:**
- Consumes: `Norse.Primitives.Ingestion.ITabularReader` (Tasks 1–2), `Norse.Primitives.Parser`/`Result<T>`/`Success<T>`/`Failure` (Svartalfheim, existing).
- Produces: `Norse.SeedTool.Mappers.RegionRow` (`internal sealed record`: `M49Code`, `Name`, `Level`, `ParentM49Code`), `Norse.SeedTool.Mappers.CountryOrAreaRow` (`internal sealed record`: `M49Code`, `IsoAlpha2Code`, `IsoAlpha3Code`, `Name`, `ParentM49Code`, `IsLeastDevelopedCountry`, `IsLandLockedDevelopingCountry`, `IsSmallIslandDevelopingState`), and `Norse.SeedTool.Mappers.UnsdM49Mapper.Map(ITabularReader reader)` returning `(IReadOnlyList<RegionRow> Regions, IReadOnlyList<CountryOrAreaRow> Countries)`. Task 7's `Program.cs`/CLI command and writer both consume these exact three names and the tuple shape.

- [ ] **Step 1: Write the failing tests**

```csharp
// Mimisbrunnr/tests/SeedTool.Tests/UnsdM49MapperTests.cs
using Norse.Primitives.Ingestion;
using Norse.SeedTool.Mappers;

namespace Norse.SeedTool.Tests;

public sealed class UnsdM49MapperTests
{
	const string Header =
		"Global Code;Global Name;Region Code;Region Name;Sub-region Code;Sub-region Name;" +
		"Intermediate Region Code;Intermediate Region Name;Country or Area;M49 Code;" +
		"ISO-alpha2 Code;ISO-alpha3 Code;Least Developed Countries (LDC);" +
		"Land Locked Developing Countries (LLDC);Small Island Developing States (SIDS)";

	static readonly string[] Rows =
	[
		"001;World;002;Africa;202;Sub-Saharan Africa;011;Western Africa;Nigeria;566;NG;NGA;;;",
		"001;World;002;Africa;015;Northern Africa;;;Algeria;012;DZ;DZA;;;",
		"001;World;;;;;;;Antarctica;010;AQ;ATA;;;",
		"001;World;002;Africa;014;Eastern Africa;;;Ethiopia;231;ET;ETH;x;x;",
	];

	[Fact]
	void Map_deduplicates_the_region_tree_and_resolves_parents()
	{
		var path = WriteFixture();
		try
		{
			using ITabularReader reader = new SepTabularReader(path, ';');
			var (regions, _) = UnsdM49Mapper.Map(reader);

			regions.Count.ShouldBe(4);
			regions.ShouldContain(r => r is { M49Code: "002", Name: "Africa", Level: "Region", ParentM49Code: null });
			regions.ShouldContain(r => r is { M49Code: "202", Name: "Sub-Saharan Africa", Level: "Subregion", ParentM49Code: "002" });
			regions.ShouldContain(r => r is { M49Code: "015", Name: "Northern Africa", Level: "Subregion", ParentM49Code: "002" });
			regions.ShouldContain(r => r is { M49Code: "014", Name: "Eastern Africa", Level: "Subregion", ParentM49Code: "002" });
		}
		finally
		{
			File.Delete(path);
		}
	}

	[Fact]
	void Map_resolves_a_three_level_deep_country()
	{
		var path = WriteFixture();
		try
		{
			using ITabularReader reader = new SepTabularReader(path, ';');
			var (regions, countries) = UnsdM49Mapper.Map(reader);

			regions.ShouldContain(r => r is { M49Code: "011", Name: "Western Africa", Level: "IntermediateRegion", ParentM49Code: "202" });
			countries.ShouldContain(c => c is { M49Code: "566", Name: "Nigeria", ParentM49Code: "011" });
		}
		finally
		{
			File.Delete(path);
		}
	}

	[Fact]
	void Map_leaves_antarctica_with_no_ancestor_at_all()
	{
		var path = WriteFixture();
		try
		{
			using ITabularReader reader = new SepTabularReader(path, ';');
			var (_, countries) = UnsdM49Mapper.Map(reader);

			countries.ShouldContain(c => c is { M49Code: "010", Name: "Antarctica", ParentM49Code: null });
		}
		finally
		{
			File.Delete(path);
		}
	}

	[Fact]
	void Map_resolves_classification_flags()
	{
		var path = WriteFixture();
		try
		{
			using ITabularReader reader = new SepTabularReader(path, ';');
			var (_, countries) = UnsdM49Mapper.Map(reader);

			countries.ShouldContain(c => c is
			{
				M49Code: "231",
				IsLeastDevelopedCountry: true,
				IsLandLockedDevelopingCountry: true,
				IsSmallIslandDevelopingState: false,
			});
			countries.ShouldContain(c => c is
			{
				M49Code: "566",
				IsLeastDevelopedCountry: false,
				IsLandLockedDevelopingCountry: false,
				IsSmallIslandDevelopingState: false,
			});
		}
		finally
		{
			File.Delete(path);
		}
	}

	[Fact]
	void Map_throws_on_a_malformed_m49_code()
	{
		var path = WriteFixture(Rows[0].Replace("566", "abc"));
		try
		{
			using ITabularReader reader = new SepTabularReader(path, ';');

			Should.Throw<InvalidOperationException>(() => UnsdM49Mapper.Map(reader));
		}
		finally
		{
			File.Delete(path);
		}
	}

	static string WriteFixture(string? replacementFirstRow = null)
	{
		string[] rows = replacementFirstRow is null ? Rows : [replacementFirstRow, .. Rows[1..]];
		var path = Path.GetTempFileName();
		File.WriteAllLines(path, [Header, .. rows]);
		return path;
	}
}
```

- [ ] **Step 2: Delete the placeholder test and run to verify failure**

```bash
rm Mimisbrunnr/tests/SeedTool.Tests/PlaceholderTests.cs
```

Run: `dotnet test Mimisbrunnr/tests/SeedTool.Tests/ -p:UseProjectReferences=true`
Expected: FAIL — `Norse.SeedTool.Mappers.UnsdM49Mapper` does not exist (CS0246).

- [ ] **Step 3: Write `RegionRow` and `CountryOrAreaRow`**

```csharp
// Mimisbrunnr/tools/SeedTool/Mappers/RegionRow.cs
namespace Norse.SeedTool.Mappers;

sealed record RegionRow(string M49Code, string Name, string Level, string? ParentM49Code);
```

```csharp
// Mimisbrunnr/tools/SeedTool/Mappers/CountryOrAreaRow.cs
namespace Norse.SeedTool.Mappers;

sealed record CountryOrAreaRow(
	string M49Code,
	string IsoAlpha2Code,
	string IsoAlpha3Code,
	string Name,
	string? ParentM49Code,
	bool IsLeastDevelopedCountry,
	bool IsLandLockedDevelopingCountry,
	bool IsSmallIslandDevelopingState);
```

- [ ] **Step 4: Write `UnsdM49Mapper`**

```csharp
// Mimisbrunnr/tools/SeedTool/Mappers/UnsdM49Mapper.cs
using System.Globalization;
using Norse.Primitives;
using Norse.Primitives.Ingestion;

namespace Norse.SeedTool.Mappers;

static class UnsdM49Mapper
{
	static readonly Dictionary<string, int> LevelRank = new(StringComparer.Ordinal)
	{
		["Region"] = 1,
		["Subregion"] = 2,
		["IntermediateRegion"] = 3,
	};

	public static (IReadOnlyList<RegionRow> Regions, IReadOnlyList<CountryOrAreaRow> Countries) Map(ITabularReader reader)
	{
		var regionCodeOrdinal = reader.Ordinal("Region Code");
		var regionNameOrdinal = reader.Ordinal("Region Name");
		var subregionCodeOrdinal = reader.Ordinal("Sub-region Code");
		var subregionNameOrdinal = reader.Ordinal("Sub-region Name");
		var intermediateCodeOrdinal = reader.Ordinal("Intermediate Region Code");
		var intermediateNameOrdinal = reader.Ordinal("Intermediate Region Name");
		var countryNameOrdinal = reader.Ordinal("Country or Area");
		var m49Ordinal = reader.Ordinal("M49 Code");
		var iso2Ordinal = reader.Ordinal("ISO-alpha2 Code");
		var iso3Ordinal = reader.Ordinal("ISO-alpha3 Code");
		var ldcOrdinal = reader.Ordinal("Least Developed Countries (LDC)");
		var llcOrdinal = reader.Ordinal("Land Locked Developing Countries (LLDC)");
		var sidsOrdinal = reader.Ordinal("Small Island Developing States (SIDS)");

		Dictionary<string, RegionRow> regions = new(StringComparer.Ordinal);
		List<CountryOrAreaRow> countries = [];
		var rowNumber = 1; // header is row 1

		while (reader.Read())
		{
			rowNumber++;

			var regionCode = reader[regionCodeOrdinal];
			var subregionCode = reader[subregionCodeOrdinal];
			var intermediateCode = reader[intermediateCodeOrdinal];

			if (!regionCode.IsEmpty)
				AddRegionIfAbsent(regions, regionCode, reader[regionNameOrdinal], "Region", null, rowNumber, "Region Code");

			if (!subregionCode.IsEmpty)
				AddRegionIfAbsent(regions, subregionCode, reader[subregionNameOrdinal], "Subregion",
					ValidateM49Code(regionCode, rowNumber, "Region Code"), rowNumber, "Sub-region Code");

			if (!intermediateCode.IsEmpty)
				AddRegionIfAbsent(regions, intermediateCode, reader[intermediateNameOrdinal], "IntermediateRegion",
					ValidateM49Code(subregionCode, rowNumber, "Sub-region Code"), rowNumber, "Intermediate Region Code");

			var parentCode =
				!intermediateCode.IsEmpty ? ValidateM49Code(intermediateCode, rowNumber, "Intermediate Region Code")
				: !subregionCode.IsEmpty ? ValidateM49Code(subregionCode, rowNumber, "Sub-region Code")
				: !regionCode.IsEmpty ? ValidateM49Code(regionCode, rowNumber, "Region Code")
				: null;

			countries.Add(new CountryOrAreaRow(
				M49Code: ValidateM49Code(reader[m49Ordinal], rowNumber, "M49 Code"),
				IsoAlpha2Code: ValidateIsoAlpha(reader[iso2Ordinal], 2, rowNumber, "ISO-alpha2 Code"),
				IsoAlpha3Code: ValidateIsoAlpha(reader[iso3Ordinal], 3, rowNumber, "ISO-alpha3 Code"),
				Name: reader[countryNameOrdinal].ToString(),
				ParentM49Code: parentCode,
				IsLeastDevelopedCountry: ValidateFlag(reader[ldcOrdinal], rowNumber, "Least Developed Countries (LDC)"),
				IsLandLockedDevelopingCountry: ValidateFlag(reader[llcOrdinal], rowNumber, "Land Locked Developing Countries (LLDC)"),
				IsSmallIslandDevelopingState: ValidateFlag(reader[sidsOrdinal], rowNumber, "Small Island Developing States (SIDS)")));
		}

		var orderedRegions = regions.Values
			.OrderBy(r => LevelRank[r.Level])
			.ThenBy(r => r.M49Code, StringComparer.Ordinal)
			.ToList();

		return (orderedRegions, countries);
	}

	static void AddRegionIfAbsent(
		Dictionary<string, RegionRow> regions,
		ReadOnlySpan<char> codeSpan,
		ReadOnlySpan<char> nameSpan,
		string level,
		string? parentM49Code,
		int rowNumber,
		string columnName)
	{
		var code = ValidateM49Code(codeSpan, rowNumber, columnName);
		if (!regions.ContainsKey(code))
			regions[code] = new RegionRow(code, nameSpan.ToString(), level, parentM49Code);
	}

	static string ValidateM49Code(ReadOnlySpan<char> span, int rowNumber, string columnName)
	{
		var result = Parser.ParseRequired<ushort>(span, CultureInfo.InvariantCulture);
		if (result.TryGetValue(out Failure failure))
			throw new InvalidOperationException($"Row {rowNumber}, column '{columnName}': {failure.Reason} (\"{failure.Input}\").");
		result.TryGetValue(out Success<ushort> success);
		return success.Value.ToString("D3", CultureInfo.InvariantCulture);
	}

	static string ValidateIsoAlpha(ReadOnlySpan<char> span, int expectedLength, int rowNumber, string columnName)
	{
		if (span.Length != expectedLength || !AllUpperAscii(span))
			throw new InvalidOperationException($"Row {rowNumber}, column '{columnName}': expected {expectedLength} uppercase letters, got \"{span}\".");
		return span.ToString();
	}

	static bool AllUpperAscii(ReadOnlySpan<char> span)
	{
		foreach (var c in span)
			if (c is < 'A' or > 'Z')
				return false;
		return true;
	}

	static bool ValidateFlag(ReadOnlySpan<char> span, int rowNumber, string columnName)
	{
		var trimmed = span.Trim();
		if (trimmed.IsEmpty)
			return false;
		if (trimmed.Equals("x", StringComparison.OrdinalIgnoreCase))
			return true;
		throw new InvalidOperationException($"Row {rowNumber}, column '{columnName}': expected \"x\" or blank, got \"{trimmed}\".");
	}
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test Mimisbrunnr/tests/SeedTool.Tests/ -p:UseProjectReferences=true`
Expected: PASS (5 tests)

- [ ] **Step 6: Commit**

```bash
git -C Mimisbrunnr add tools/SeedTool/Mappers tests/SeedTool.Tests
git -C Mimisbrunnr rm tests/SeedTool.Tests/PlaceholderTests.cs
git -C Mimisbrunnr commit -m "feat: add UnsdM49Mapper, mapping the raw UNSD CSV to Region/CountryOrArea rows"
```

---

### Task 7: Mimisbrunnr — `SeedTool` CLI command, TSV writer, and real-file integration test

**Files:**
- Create: `Mimisbrunnr/tools/SeedTool/Mappers/UnsdM49Writer.cs`
- Create: `Mimisbrunnr/tools/SeedTool/Commands/GenerateUnsdM49Command.cs`
- Modify: `Mimisbrunnr/tools/SeedTool/Program.cs`
- Create: `Mimisbrunnr/tests/SeedTool.Tests/UnsdM49RealFileTests.cs`

**Interfaces:**
- Consumes: `UnsdM49Mapper.Map` (Task 6).
- Produces: `Norse.SeedTool.Mappers.UnsdM49Writer.WriteRegions(string path, IReadOnlyList<RegionRow> regions)` / `.WriteCountries(string path, IReadOnlyList<CountryOrAreaRow> countries)`; `Norse.SeedTool.Commands.GenerateUnsdM49Command` — the Spectre.Console.Cli command Task 8 runs for real.

- [ ] **Step 1: Write the failing integration test against the real 248-row source file**

```csharp
// Mimisbrunnr/tests/SeedTool.Tests/UnsdM49RealFileTests.cs
using Norse.Primitives.Ingestion;
using Norse.SeedTool.Mappers;

namespace Norse.SeedTool.Tests;

public sealed class UnsdM49RealFileTests
{
	const string SourcePath = "../../../../../seeds/raw/UNSD — Methodology.csv";

	[Fact]
	void Map_produces_the_expected_counts_and_known_rows_from_the_real_source()
	{
		using ITabularReader reader = new SepTabularReader(SourcePath, ';');
		var (regions, countries) = UnsdM49Mapper.Map(reader);

		// 5 Regions + 17 Sub-regions + 7 Intermediate Regions, per the approved M49 spec's
		// verified data facts (Glitnir/docs/Mimisbrunnr/specs/2026-07-04-unsd-m49-reference-data-design.md §1).
		regions.Count.ShouldBe(29);
		countries.Count.ShouldBe(248);

		countries.ShouldContain(c => c is { M49Code: "566", Name: "Nigeria", IsoAlpha2Code: "NG", IsoAlpha3Code: "NGA" });
		countries.ShouldContain(c => c is { M49Code: "010", Name: "Antarctica", ParentM49Code: null });
	}
}
```

(The relative path climbs from `tests/SeedTool.Tests/bin/<config>/net11.0/` back to the repo root, then into `seeds/raw/`. If the test runner's working directory differs, replace with `Path.Combine(AppContext.BaseDirectory, "../../../../../seeds/raw/UNSD — Methodology.csv")` — same target, resolved from the output directory instead of an assumed CWD.)

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test Mimisbrunnr/tests/SeedTool.Tests/ -p:UseProjectReferences=true`
Expected: FAIL — either the path doesn't resolve yet (adjust per the note above until `File.Exists` on that path is confirmed manually first) or the counts don't match while diagnosing path/ordinal issues. Once the path is confirmed correct, this test should already pass against `Map` alone (it doesn't need the writer) — if so, proceed to Step 3 anyway to add the writer, which is what the rest of this task and Task 8 need.

- [ ] **Step 3: Write `UnsdM49Writer`**

```csharp
// Mimisbrunnr/tools/SeedTool/Mappers/UnsdM49Writer.cs
using System.Text;

namespace Norse.SeedTool.Mappers;

static class UnsdM49Writer
{
	public static void WriteRegions(string path, IReadOnlyList<RegionRow> regions)
	{
		using StreamWriter writer = new(path, append: false, Encoding.UTF8);
		writer.WriteLine("M49Code\tName\tLevel\tParentM49Code");
		foreach (var region in regions)
			writer.WriteLine($"{region.M49Code}\t{region.Name}\t{region.Level}\t{region.ParentM49Code}");
	}

	public static void WriteCountries(string path, IReadOnlyList<CountryOrAreaRow> countries)
	{
		using StreamWriter writer = new(path, append: false, Encoding.UTF8);
		writer.WriteLine("M49Code\tIsoAlpha2Code\tIsoAlpha3Code\tName\tParentM49Code\tIsLeastDevelopedCountry\tIsLandLockedDevelopingCountry\tIsSmallIslandDevelopingState");
		foreach (var country in countries)
			writer.WriteLine(string.Join('\t',
				country.M49Code,
				country.IsoAlpha2Code,
				country.IsoAlpha3Code,
				country.Name,
				country.ParentM49Code,
				FormatFlag(country.IsLeastDevelopedCountry),
				FormatFlag(country.IsLandLockedDevelopingCountry),
				FormatFlag(country.IsSmallIslandDevelopingState)));
	}

	static string FormatFlag(bool value) =>
		value ? "true" : "false";
}
```

- [ ] **Step 4: Write the CLI command**

```csharp
// Mimisbrunnr/tools/SeedTool/Commands/GenerateUnsdM49Command.cs
using System.ComponentModel;
using Norse.Primitives.Ingestion;
using Norse.SeedTool.Mappers;
using Spectre.Console.Cli;

namespace Norse.SeedTool.Commands;

sealed class GenerateUnsdM49Command : Command<GenerateUnsdM49Command.Settings>
{
	public sealed class Settings : CommandSettings
	{
		[CommandArgument(0, "[sourcePath]")]
		[Description("Path to the raw UNSD Methodology CSV.")]
		public string SourcePath { get; init; } = "seeds/raw/UNSD — Methodology.csv";

		[CommandArgument(1, "[outputDirectory]")]
		[Description("Directory to write region.tsv and country-or-area.tsv into.")]
		public string OutputDirectory { get; init; } = "seeds";
	}

	public override int Execute(CommandContext context, Settings settings)
	{
		using ITabularReader reader = new SepTabularReader(settings.SourcePath, ';');
		var (regions, countries) = UnsdM49Mapper.Map(reader);

		var regionPath = Path.Combine(settings.OutputDirectory, "region.tsv");
		var countryPath = Path.Combine(settings.OutputDirectory, "country-or-area.tsv");

		UnsdM49Writer.WriteRegions(regionPath, regions);
		UnsdM49Writer.WriteCountries(countryPath, countries);

		Console.WriteLine($"Wrote {regions.Count} region rows to {regionPath}");
		Console.WriteLine($"Wrote {countries.Count} country rows to {countryPath}");
		return 0;
	}
}
```

```csharp
// Mimisbrunnr/tools/SeedTool/Program.cs
using Norse.SeedTool.Commands;
using Spectre.Console.Cli;

CommandApp<GenerateUnsdM49Command> app = new();
return app.Run(args);
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test Mimisbrunnr/tests/SeedTool.Tests/ -p:UseProjectReferences=true`
Expected: PASS (7 tests total: 5 from Task 6, 1 real-file test, 1 already covered — confirm the exact count printed matches files added so far).

- [ ] **Step 6: Commit**

```bash
git -C Mimisbrunnr add tools/SeedTool tests/SeedTool.Tests/UnsdM49RealFileTests.cs
git -C Mimisbrunnr commit -m "feat: add UnsdM49Writer and the GenerateUnsdM49Command CLI entry point"
```

---

### Task 8: Mimisbrunnr — generate the real seed TSVs and verify reproducibility

**Files:**
- Create: `Mimisbrunnr/seeds/region.tsv` (generated, then committed)
- Create: `Mimisbrunnr/seeds/country-or-area.tsv` (generated, then committed)

**Interfaces:** None new — this task runs Task 7's CLI for real and commits its output as Mimisbrunnr's actual seed data.

- [ ] **Step 1: Run the tool for real**

Run: `dotnet run --project Mimisbrunnr/tools/SeedTool -p:UseProjectReferences=true -- "Mimisbrunnr/seeds/raw/UNSD — Methodology.csv" "Mimisbrunnr/seeds"`
Expected: prints `Wrote 29 region rows to Mimisbrunnr/seeds/region.tsv` and `Wrote 248 country rows to Mimisbrunnr/seeds/country-or-area.tsv`; both files now exist.

- [ ] **Step 2: Verify reproducibility**

```bash
cp Mimisbrunnr/seeds/region.tsv /tmp/region.tsv.first
cp Mimisbrunnr/seeds/country-or-area.tsv /tmp/country-or-area.tsv.first
```

Run the Step 1 command again, then:

```bash
diff /tmp/region.tsv.first Mimisbrunnr/seeds/region.tsv
diff /tmp/country-or-area.tsv.first Mimisbrunnr/seeds/country-or-area.tsv
```

Expected: both `diff`s produce no output (byte-identical), proving the M49 spec's §7 reproducibility criterion.

- [ ] **Step 3: Verify fail-loud on a corrupted copy**

```bash
cp "Mimisbrunnr/seeds/raw/UNSD — Methodology.csv" /tmp/corrupted.csv
```

Edit `/tmp/corrupted.csv`: change Nigeria's `M49 Code` field from `566` to `abc` (one cell, one row).

Run: `dotnet run --project Mimisbrunnr/tools/SeedTool -p:UseProjectReferences=true -- /tmp/corrupted.csv /tmp/corrupted-output`
Expected: non-zero exit, an `InvalidOperationException` message naming the row number, `M49 Code`, and `"abc"`; `/tmp/corrupted-output` is never created (no partial output — confirm with `ls /tmp/corrupted-output` reporting "No such file or directory").

- [ ] **Step 4: Commit the generated seed data**

```bash
git -C Mimisbrunnr add seeds/region.tsv seeds/country-or-area.tsv
git -C Mimisbrunnr commit -m "chore: generate region.tsv and country-or-area.tsv from the UNSD M49 source via SeedTool"
```

- [ ] **Step 5: Report status**

Both realms are done: Svartalfheim ships `Norse.Primitives.Ingestion` (Tasks 1–4), Mimisbrunnr's `SeedTool` produces real, reproducible, fail-loud-verified seed TSVs (Tasks 5–8). The M49 spec's own EF entity/migration/seed-contributor work (consuming these two TSVs to actually populate `norse_referencedata`) is separate, later work — out of scope here, per this plan's own scope (the conversion tool, not the runtime seed path).

---

## Self-Review

**Spec coverage:** §1.1 (Ingestion has no dependency on Primitives) — Task 1's `Primitives.Ingestion.csproj` has no `ProjectReference`/`NorseRef` to `Primitives`. §1.2 (one contract, two backends) — Tasks 1–2. §1.3 (structural throws vs. `Result<T>` scalar failures) — Task 1's malformed-row test, Task 2's corrupt-workbook test, Task 6's `ValidateM49Code` routing through `Parser.ParseRequired<T>`. §1.4 (fail-loud, no partial output) — Task 6's mapper throws before any row is added past the bad one; Task 8 Step 3 proves no output file is written on failure. §1.5 (dev-only, never AOT-published) — Task 5's `tools/Directory.Build.props` sets `IsAotCompatible=false`/`IsPackable=false` explicitly. §1.6 (per-realm tool) — `SeedTool` lives in Mimisbrunnr, not a shared repo. §2 (`Norse.Primitives.Ingestion` project shape and AOT verification) — Tasks 1–4. §3 (`SeedTool` shape, data flow, TSV columns) — Tasks 5–8, column shapes match §4 of the M49 spec exactly. §5 success criteria — Task 3 (AOT smoke), Task 1/2 (both readers satisfy the contract; malformed fixtures throw), Task 8 Steps 2–3 (reproducibility, fail-loud).

**Placeholder scan:** No TBDs. Every step has complete, concrete code. Task 3 Step 3's AOT-warning contingency is stated as an explicit fallback with a concrete action (capture the warning text, re-run the smoke binary as the real behavioral proof), not a vague "handle appropriately."

**Type consistency:** `ITabularReader` (Task 1) is satisfied identically by `SepTabularReader` (Task 1) and `ExcelTabularReader` (Task 2) — same three members plus the indexer, checked in both test files. `UnsdM49Mapper.Map`'s return tuple shape (`(IReadOnlyList<RegionRow>, IReadOnlyList<CountryOrAreaRow>)`, Task 6) is consumed identically by Task 6's own tests, Task 7's integration test, and Task 7's `GenerateUnsdM49Command`. `RegionRow`/`CountryOrAreaRow`'s field names are used identically across Task 6's mapper, Task 6's tests, and Task 7's writer — no renamed field anywhere downstream.
