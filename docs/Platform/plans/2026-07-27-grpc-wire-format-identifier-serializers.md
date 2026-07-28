# gRPC Wire Format: Identifier Serializers and Code-First Reflection — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (the platform default, per Glitnir CLAUDE.md §2.8) paired with superpowers:test-driven-development — or superpowers:executing-plans as the narrow separate-session fallback. Steps use checkbox (`- [ ]`) syntax for tracking.

**Spec:** `../specs/2026-07-27-grpc-wire-format-identifier-serializers-design.md` (ratified; §5.1 spike verdict already folded in — every mechanism below is spike-proven, none is speculative)

**Goal:** `CompatibilityLevel.Level300` becomes the platform's explicit model-wide protobuf-net setting, `Guid`/`DeterministicGuid`/`SequentialGuid` hit the wire as bare 16-byte RFC 9562 `bytes` fields, and Yggdrasil's gRPC reflection endpoint actually serves the code-first Norse surface.

**Architecture:** New shared Midgard project `Infrastructure.Web.Grpc` (assembly `Norse.Infrastructure.Web.Grpc`) carries one public entry point — `IdentifierSerializers.Register(RuntimeTypeModel)` — that sets Level 300, subscribes an `AfterApplyDefaultBehaviour` sweep forcing `DataFormat.FixedSize` on every `Guid`/`Guid?` member of every type entering the model, and attaches custom `ISerializer<T>` scalar serializers for the two Svartálfheim identifier types. Both existing wiring generators emit one call to it. Yggdrasil swaps the stock reflection middleware for protobuf-net.Grpc's code-first implementation.

**Tech Stack:** .NET 11 preview 6 / C# 15 (`net11.0`), protobuf-net 3.x (`Version="3.*"`, resolves ≥ 3.2.56), protobuf-net.Grpc.AspNetCore.Reflection 1.2.2, xUnit v3 on MTP + Shouldly.

## Global Constraints

- **Wire law (spec §2, verbatim):** identifiers are a bare `bytes` field, 16 bytes, RFC 9562 order — bit-identical to protobuf-net's Level 300 + `DataFormat.FixedSize` form. Golden vector: `12345678-9abc-def0-1234-56789abcdef0` → payload `0A10123456789ABCDEF0123456789ABCDEF0`. SQL Server byte order never crosses the wire.
- **Realm sanctity (spec §8):** Svartálfheim, Asgard, Urðarbrunnr, and every realm contract assembly are untouched. No `[ProtoMember]` or protobuf-net reference enters any contract assembly.
- **Immutable files — halt and ask if a task seems to need them changed:** every `Directory.Build.props`/`Directory.Build.targets` (root, `src/`, `gen/`, `tests/`), every `.editorconfig`, `global.json`, `nuget.config` — all scatter-owned by Ginnungagap.
- **Git:** Midgard and Yggdrasil work happens on the feature branches named in their tasks; committing there is permitted (local branch, never pushed, never master). Run `git -C <repo> branch --show-current` before **every** commit. Bifröst repo files (`Bifrost.slnx`) and Glitnir: stage only, never commit, never branch.
- **Warnings are errors platform-wide.** A single warning fails the build.
- **House style (Glitnir `docs/house-rules.md` — already encoded in every code block below):** tabs; target-typed `new()` for construction, `var` for returns; expression bodies with arrow-on-declaration-line; collection expressions; no string concatenation; `sealed` by default with accessibility modifiers omitted at default; XML docs on all public src members (CS1591 is an error in src); `ConfigureAwait(false)` in src, never tests.
- **Tests:** xUnit v3 on MTP — **never `dotnet test` a project with zero tests** (the run fails); Shouldly asserts; Shouldly/Xunit usings are globally injected — never re-add per file; test classes `public sealed`; test methods bare `void` (no access modifier), sentence-shaped names with underscores. `InternalsVisibleTo` for `$(AssemblyName).Tests` already flows from `src/Directory.Build.props`. Filter syntax: `dotnet test <proj> -- --filter-class "*.ClassName"` (VSTest `--filter` does not work).
- **All commands below run from the Bifröst root.** Use `env -C` / `git -C` for other directories — never `cd &&` (broken `_update_prompt` hook in this environment).
- **Ship sequencing note:** Yggdrasil builds against Midgard via project references inside Bifröst (`UseProjectReferences` Choose), so Tasks 5–6 work locally immediately after Task 4. Yggdrasil **CI** (package mode) additionally needs Midgard merged/tagged/published and the Yggdrasil pins bumped — that ship ceremony is Buvy's, between the Midgard PR and the Yggdrasil PR.

## File Structure

**Midgard** (branch `feature/grpc-wire-format`):
- Create: `Midgard/src/Infrastructure.Web.Grpc/Infrastructure.Web.Grpc.csproj` — project shell; NorseRef to Svartálfheim `Primitives`, protobuf-net
- Create: `Midgard/src/Infrastructure.Web.Grpc/IdentifierSerializers.cs` — the one public entry point
- Create: `Midgard/src/Infrastructure.Web.Grpc/GuidWire.cs` — shared 16-byte RFC read/write helper (internal)
- Create: `Midgard/src/Infrastructure.Web.Grpc/SequentialGuidSerializer.cs` — custom scalar serializer (internal)
- Create: `Midgard/src/Infrastructure.Web.Grpc/DeterministicGuidSerializer.cs` — custom scalar serializer (internal)
- Create: `Midgard/tests/Infrastructure.Web.Grpc.Tests/Infrastructure.Web.Grpc.Tests.csproj`
- Create: `Midgard/tests/Infrastructure.Web.Grpc.Tests/TestModel.cs` — shared serialize/deserialize helpers
- Create: `Midgard/tests/Infrastructure.Web.Grpc.Tests/IdentifierSerializersTests.cs`
- Create: `Midgard/tests/Infrastructure.Web.Grpc.Tests/SequentialGuidSerializerTests.cs`
- Create: `Midgard/tests/Infrastructure.Web.Grpc.Tests/DeterministicGuidSerializerTests.cs`
- Modify: `Midgard/Midgard.slnx` — two `<Project>` entries
- Modify: `Midgard/src/Infrastructure.Web.Client/Infrastructure.Web.Client.csproj` — ProjectReference + Description clause
- Modify: `Midgard/src/Infrastructure.Web.Server/Infrastructure.Web.Server.csproj` — ProjectReference + Description clause
- Modify: `Midgard/gen/Infrastructure.Web.Client.Generator/ClientRegistrationEmitter.cs` — one emitted line
- Modify: `Midgard/gen/Infrastructure.Web.Server.Generator/ServerRegistrationEmitter.cs` — one emitted line
- Modify: `Midgard/tests/Infrastructure.Web.Client.Generator.Tests/GrpcClientRegistrationGeneratorTests.cs` — one test
- Modify: `Midgard/tests/Infrastructure.Web.Server.Generator.Tests/GrpcServerRegistrationGeneratorTests.cs` — one test

**Yggdrasil** (branch `feature/code-first-reflection`):
- Modify: `Yggdrasil/Directory.Packages.props` — reflection package swap + census pin
- Modify: `Yggdrasil/src/Hosting.Web.Server/Hosting.Web.Server.csproj` — reflection package swap
- Modify: `Yggdrasil/src/Hosting.Web.Server/Program.cs` — two call swaps + using

**Bifröst / Glitnir / docs** (stage only):
- Modify: `Bifrost.slnx` — two `<Project>` entries (Midgard folder)
- Modify: `Glitnir/docs/Platform/specs/2026-07-27-grpc-wire-format-identifier-serializers-design.md` — verification amendment
- Modify: `Midgard/CLAUDE.md` + `Midgard/README.md` — new-project boy-scout sync (same Midgard branch)

---

### Task 1: `Infrastructure.Web.Grpc` scaffold + `Guid` wire law

**Files:**
- Create: `Midgard/src/Infrastructure.Web.Grpc/Infrastructure.Web.Grpc.csproj`
- Create: `Midgard/src/Infrastructure.Web.Grpc/IdentifierSerializers.cs`
- Create: `Midgard/tests/Infrastructure.Web.Grpc.Tests/Infrastructure.Web.Grpc.Tests.csproj`
- Create: `Midgard/tests/Infrastructure.Web.Grpc.Tests/TestModel.cs`
- Create: `Midgard/tests/Infrastructure.Web.Grpc.Tests/IdentifierSerializersTests.cs`
- Modify: `Midgard/Midgard.slnx`

**Interfaces:**
- Consumes: `ProtoBuf.Meta.RuntimeTypeModel`, `ProtoBuf.Meta.TypeAddedEventArgs`, `ProtoBuf.DataFormat`, `ProtoBuf.Meta.CompatibilityLevel` (protobuf-net 3.x).
- Produces: `public static class IdentifierSerializers` in namespace `Norse.Infrastructure.Web.Grpc` with `public static void Register(RuntimeTypeModel model)` — idempotent per model; Tasks 2–4 extend and call it. Test helper `static class TestModel` with `internal static RuntimeTypeModel Create()`, `internal static byte[] Serialize<T>(TypeModel, T)`, `internal static T Deserialize<T>(TypeModel, byte[])`.

- [ ] **Step 1: Create the Midgard feature branch**

```bash
git -C Midgard checkout -b feature/grpc-wire-format
```

- [ ] **Step 2: Create the two project shells and wire them into the solution**

`Midgard/src/Infrastructure.Web.Grpc/Infrastructure.Web.Grpc.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
	<PropertyGroup>
		<Description>Norse.Infrastructure.Web.Grpc: the shared gRPC wire-format recipe consumed at all three levels — MAUI, WASM client, and server. IdentifierSerializers applies the Norse wire law to a protobuf-net RuntimeTypeModel: CompatibilityLevel 300 as the model default, every Guid member swept to DataFormat.FixedSize (a bare bytes field of 16 bytes in RFC 9562 order — never the legacy bcl.Guid encoding, never the 36-character string), and custom scalar serializers putting SequentialGuid and DeterministicGuid on the wire in the same canonical form. Nothing app-specific lives here: gRPC-Web browser plumbing, server endpoint mapping, and MAUI transport wiring are host concerns.</Description>
	</PropertyGroup>
	<ItemGroup>
		<NorseRef Include="Primitives">
			<Repo>Svartalfheim</Repo>
		</NorseRef>
		<PackageReference Include="protobuf-net" Version="3.*" />
	</ItemGroup>
</Project>
```

`Midgard/tests/Infrastructure.Web.Grpc.Tests/Infrastructure.Web.Grpc.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
	<ItemGroup>
		<ProjectReference Include="../../src/Infrastructure.Web.Grpc/Infrastructure.Web.Grpc.csproj" />
	</ItemGroup>
</Project>
```

In `Midgard/Midgard.slnx`, add to the `/src/` folder (after the `Infrastructure.Web.Client` entry):

```xml
		<Project Path="src/Infrastructure.Web.Grpc/Infrastructure.Web.Grpc.csproj" />
```

and to the `/tests/` folder (after the `Infrastructure.Web.Client.Generator.Tests` entry):

```xml
		<Project Path="tests/Infrastructure.Web.Grpc.Tests/Infrastructure.Web.Grpc.Tests.csproj" />
```

- [ ] **Step 3: Write the failing tests**

`Midgard/tests/Infrastructure.Web.Grpc.Tests/TestModel.cs`:

```csharp
using ProtoBuf.Meta;

namespace Norse.Infrastructure.Web.Grpc.Tests;

static class TestModel
{
	internal static RuntimeTypeModel Create()
	{
		var model = RuntimeTypeModel.Create();
		IdentifierSerializers.Register(model);
		return model;
	}

	internal static byte[] Serialize<T>(TypeModel model, T value)
	{
		using MemoryStream stream = new();
		model.Serialize(stream, value!);
		return stream.ToArray();
	}

	internal static T Deserialize<T>(TypeModel model, byte[] payload) =>
		(T)model.Deserialize(new MemoryStream(payload), null, typeof(T))!;
}
```

`Midgard/tests/Infrastructure.Web.Grpc.Tests/IdentifierSerializersTests.cs`:

```csharp
using ProtoBuf;
using ProtoBuf.Meta;

namespace Norse.Infrastructure.Web.Grpc.Tests;

public sealed class IdentifierSerializersTests
{
	static readonly Guid KnownGuid = new("12345678-9abc-def0-1234-56789abcdef0");
	const string KnownWireHex = "0A10123456789ABCDEF0123456789ABCDEF0";

	[Fact]
	void Serializes_a_Guid_member_as_sixteen_rfc_9562_bytes_on_an_auto_discovered_type()
	{
		var model = TestModel.Create();
		var payload = TestModel.Serialize(model, new GuidEnvelope { Id = KnownGuid });
		Convert.ToHexString(payload).ShouldBe(KnownWireHex);
	}

	[Fact]
	void Matches_protobuf_nets_own_level_300_fixed_size_form_bit_for_bit()
	{
		var reference = RuntimeTypeModel.Create();
		reference.DefaultCompatibilityLevel = CompatibilityLevel.Level300;
		var expected = TestModel.Serialize(reference, new FixedSizeGuidEnvelope { Id = KnownGuid });
		var actual = TestModel.Serialize(TestModel.Create(), new GuidEnvelope { Id = KnownGuid });
		actual.ShouldBe(expected);
	}

	[Fact]
	void Sweeps_a_type_added_explicitly_to_the_model()
	{
		var model = TestModel.Create();
		model.Add(typeof(GuidEnvelope));
		var payload = TestModel.Serialize(model, new GuidEnvelope { Id = KnownGuid });
		Convert.ToHexString(payload).ShouldBe(KnownWireHex);
	}

	[Fact]
	void Sweeps_nullable_Guid_members()
	{
		var model = TestModel.Create();
		var payload = TestModel.Serialize(model, new NullableGuidEnvelope { Id = KnownGuid });
		Convert.ToHexString(payload).ShouldBe(KnownWireHex);
	}

	[Fact]
	void Round_trips_a_null_nullable_Guid_member_as_null()
	{
		var model = TestModel.Create();
		var payload = TestModel.Serialize(model, new NullableGuidEnvelope());
		TestModel.Deserialize<NullableGuidEnvelope>(model, payload).Id.ShouldBeNull();
	}

	[Fact]
	void Round_trips_Guid_Empty()
	{
		var model = TestModel.Create();
		var payload = TestModel.Serialize(model, new GuidEnvelope { Id = Guid.Empty });
		TestModel.Deserialize<GuidEnvelope>(model, payload).Id.ShouldBe(Guid.Empty);
	}

	[Fact]
	void Sets_compatibility_level_300_as_the_model_default() =>
		TestModel.Create().DefaultCompatibilityLevel.ShouldBe(CompatibilityLevel.Level300);

	[Fact]
	void Registers_idempotently_when_called_twice_on_one_model()
	{
		var model = TestModel.Create();
		Should.NotThrow(() => IdentifierSerializers.Register(model));
		var payload = TestModel.Serialize(model, new GuidEnvelope { Id = KnownGuid });
		Convert.ToHexString(payload).ShouldBe(KnownWireHex);
	}

	[Fact]
	void Renders_Guid_members_as_bytes_fields_in_the_schema()
	{
		var model = TestModel.Create();
		model.Add(typeof(GuidEnvelope));
		model.GetSchema(typeof(GuidEnvelope), ProtoSyntax.Proto3).ShouldContain("bytes Id = 1;");
	}
}

[ProtoContract]
public sealed class GuidEnvelope
{
	[ProtoMember(1)]
	public Guid Id { get; set; }
}

[ProtoContract]
public sealed class NullableGuidEnvelope
{
	[ProtoMember(1)]
	public Guid? Id { get; set; }
}

[ProtoContract]
public sealed class FixedSizeGuidEnvelope
{
	[ProtoMember(1, DataFormat = DataFormat.FixedSize)]
	public Guid Id { get; set; }
}
```

- [ ] **Step 4: Run the tests to verify they fail**

```bash
dotnet test Midgard/tests/Infrastructure.Web.Grpc.Tests/Infrastructure.Web.Grpc.Tests.csproj
```

Expected: build FAILS with CS0103 (`IdentifierSerializers` does not exist).

- [ ] **Step 5: Implement `IdentifierSerializers`**

`Midgard/src/Infrastructure.Web.Grpc/IdentifierSerializers.cs`:

```csharp
using System.Runtime.CompilerServices;
using ProtoBuf;
using ProtoBuf.Meta;

namespace Norse.Infrastructure.Web.Grpc;

/// <summary>
/// Applies the Norse wire law to a protobuf-net <see cref="RuntimeTypeModel"/>:
/// <see cref="CompatibilityLevel.Level300"/> as the model default, and every identifier on the wire as a
/// bare <c>bytes</c> field carrying 16 bytes in RFC 9562 order — never the legacy <c>bcl.Guid</c>
/// encoding, never the 36-character string.
/// </summary>
public static class IdentifierSerializers
{
	static readonly ConditionalWeakTable<RuntimeTypeModel, RuntimeTypeModel> _registered = new();

	/// <summary>
	/// Registers the wire law on <paramref name="model"/>. Idempotent per model. Must run before any
	/// contract type enters the model — <see cref="RuntimeTypeModel.DefaultCompatibilityLevel"/> cannot
	/// change once types have been added, and protobuf-net fails loudly if it is attempted.
	/// </summary>
	public static void Register(RuntimeTypeModel model)
	{
		ArgumentNullException.ThrowIfNull(model);
		if (!_registered.TryAdd(model, model))
			return;

		model.DefaultCompatibilityLevel = CompatibilityLevel.Level300;
		model.AfterApplyDefaultBehaviour += SweepGuidMembers;
	}

	static void SweepGuidMembers(object? sender, TypeAddedEventArgs e)
	{
		foreach (var field in e.MetaType.GetFields())
		{
			if (field.MemberType == typeof(Guid) || field.MemberType == typeof(Guid?))
				field.DataFormat = DataFormat.FixedSize;
		}
	}
}
```

(Spike-proven notes: `Add(typeof(Guid))` is structurally impossible in protobuf-net v3 — the `MetaType` ctor throws for inbuilt types — which is exactly why the sweep exists; `AfterApplyDefaultBehaviour` fires for auto-discovered types too, so the sweep is hole-free; `ValueMember.DataFormat` setting the same value twice is a no-op, so double-firing is harmless.)

- [ ] **Step 6: Run the tests to verify they pass**

```bash
dotnet test Midgard/tests/Infrastructure.Web.Grpc.Tests/Infrastructure.Web.Grpc.Tests.csproj
```

Expected: PASS, 9 tests.

- [ ] **Step 7: Commit**

```bash
git -C Midgard branch --show-current   # must print feature/grpc-wire-format
git -C Midgard add src/Infrastructure.Web.Grpc tests/Infrastructure.Web.Grpc.Tests Midgard.slnx
git -C Midgard commit -m "Add Infrastructure.Web.Grpc: CompatibilityLevel 300 + Guid FixedSize sweep"
```

---

### Task 2: `SequentialGuidSerializer`

**Files:**
- Create: `Midgard/src/Infrastructure.Web.Grpc/GuidWire.cs`
- Create: `Midgard/src/Infrastructure.Web.Grpc/SequentialGuidSerializer.cs`
- Modify: `Midgard/src/Infrastructure.Web.Grpc/IdentifierSerializers.cs`
- Create: `Midgard/tests/Infrastructure.Web.Grpc.Tests/SequentialGuidSerializerTests.cs`

**Interfaces:**
- Consumes: `TestModel` helpers (Task 1); Svartálfheim `Norse.Primitives.Identifiers` — `SequentialGuid(Guid, GuidByteOrder)` (throws `ArgumentException` on non-v7 bits), `SequentialGuid.ToRfcOrder()`/`.ToSqlOrder()`, `SequentialGuid.Value`, `GuidByteOrder.Rfc9562`; `ProtoBuf.Serializers.ISerializer<T>`.
- Produces: `static class GuidWire` with `internal static Guid Read(ref ProtoReader.State)` (throws `InvalidDataException` on length ≠ 16) and `internal static void Write(ref ProtoWriter.State, in Guid)` — Task 3 reuses both. `sealed class SequentialGuidSerializer : ISerializer<SequentialGuid>, ISerializer<SequentialGuid?>` registered inside `IdentifierSerializers.Register`.

- [ ] **Step 1: Write the failing tests**

`Midgard/tests/Infrastructure.Web.Grpc.Tests/SequentialGuidSerializerTests.cs`:

```csharp
using Norse.Primitives.Identifiers;
using ProtoBuf;

namespace Norse.Infrastructure.Web.Grpc.Tests;

public sealed class SequentialGuidSerializerTests
{
	// The RFC 9562 §A.6 example UUIDv7.
	static readonly Guid KnownV7 = new("017f22e2-79b0-7cc3-98c4-dc0c0c07398f");
	const string KnownWireHex = "0A10017F22E279B07CC398C4DC0C0C07398F";

	[Fact]
	void Serializes_as_sixteen_rfc_9562_bytes()
	{
		var model = TestModel.Create();
		var payload = TestModel.Serialize(model,
			new SequentialGuidEnvelope { Id = new(KnownV7, GuidByteOrder.Rfc9562) });
		Convert.ToHexString(payload).ShouldBe(KnownWireHex);
	}

	[Fact]
	void Normalizes_a_sql_ordered_value_to_rfc_order_on_the_wire()
	{
		var model = TestModel.Create();
		var sqlOrdered = new SequentialGuid(KnownV7, GuidByteOrder.Rfc9562).ToSqlOrder();
		var payload = TestModel.Serialize(model, new SequentialGuidEnvelope { Id = sqlOrdered });
		Convert.ToHexString(payload).ShouldBe(KnownWireHex);
	}

	[Fact]
	void Rehydrates_in_rfc_order_and_equal_to_the_original()
	{
		var model = TestModel.Create();
		SequentialGuid original = new(KnownV7, GuidByteOrder.Rfc9562);
		var payload = TestModel.Serialize(model, new SequentialGuidEnvelope { Id = original });
		var back = TestModel.Deserialize<SequentialGuidEnvelope>(model, payload).Id;
		back.Order.ShouldBe(GuidByteOrder.Rfc9562);
		back.ShouldBe(original);
	}

	[Fact]
	void Round_trips_a_nullable_member()
	{
		var model = TestModel.Create();
		var payload = TestModel.Serialize(model,
			new NullableSequentialGuidEnvelope { Id = new(KnownV7, GuidByteOrder.Rfc9562) });
		TestModel.Deserialize<NullableSequentialGuidEnvelope>(model, payload)
			.Id.ShouldBe(new SequentialGuid(KnownV7, GuidByteOrder.Rfc9562));
	}

	[Fact]
	void Round_trips_a_null_nullable_member_as_null()
	{
		var model = TestModel.Create();
		var payload = TestModel.Serialize(model, new NullableSequentialGuidEnvelope());
		TestModel.Deserialize<NullableSequentialGuidEnvelope>(model, payload).Id.ShouldBeNull();
	}

	[Fact]
	void Throws_on_a_truncated_payload()
	{
		var model = TestModel.Create();
		byte[] truncated = [0x0A, 0x0F, .. new byte[15]];
		Should.Throw<InvalidDataException>(() =>
			TestModel.Deserialize<SequentialGuidEnvelope>(model, truncated));
	}

	[Fact]
	void Throws_on_sixteen_bytes_that_are_not_a_version_7_uuid()
	{
		var model = TestModel.Create();
		byte[] allZero = [0x0A, 0x10, .. new byte[16]];
		Should.Throw<ArgumentException>(() =>
			TestModel.Deserialize<SequentialGuidEnvelope>(model, allZero));
	}
}

[ProtoContract]
public sealed class SequentialGuidEnvelope
{
	[ProtoMember(1)]
	public SequentialGuid Id { get; set; }
}

[ProtoContract]
public sealed class NullableSequentialGuidEnvelope
{
	[ProtoMember(1)]
	public SequentialGuid? Id { get; set; }
}
```

(Exception assertions are exact: spike round 4 proved exceptions thrown inside a custom `ISerializer.Read` surface **unwrapped** at `Deserialize`.)

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet test Midgard/tests/Infrastructure.Web.Grpc.Tests/Infrastructure.Web.Grpc.Tests.csproj
```

Expected: build FAILS (no `SequentialGuidSerializer` registered → actually compiles, then the serialization tests FAIL: protobuf-net has no handling for `SequentialGuid`). Either failure mode is the expected red.

- [ ] **Step 3: Implement `GuidWire` and `SequentialGuidSerializer`, and register**

`Midgard/src/Infrastructure.Web.Grpc/GuidWire.cs`:

```csharp
using ProtoBuf;

namespace Norse.Infrastructure.Web.Grpc;

/// <summary>Shared read/write of the canonical identifier payload: a bare <c>bytes</c> field of 16 bytes in RFC 9562 order.</summary>
static class GuidWire
{
	internal static Guid Read(ref ProtoReader.State state)
	{
		var bytes = state.AppendBytes(null);
		return bytes.Length == 16 ?
			new(bytes, bigEndian: true) :
			throw new InvalidDataException($"Expected a 16-byte RFC 9562 UUID payload, got {bytes.Length} bytes.");
	}

	internal static void Write(ref ProtoWriter.State state, in Guid value)
	{
		var bytes = new byte[16];
		value.TryWriteBytes(bytes, bigEndian: true, out _);
		state.WriteBytes(bytes);
	}
}
```

`Midgard/src/Infrastructure.Web.Grpc/SequentialGuidSerializer.cs`:

```csharp
using Norse.Primitives.Identifiers;
using ProtoBuf;
using ProtoBuf.Serializers;

namespace Norse.Infrastructure.Web.Grpc;

/// <summary>
/// Puts <see cref="SequentialGuid"/> on the wire as the canonical 16-byte RFC 9562 <c>bytes</c> payload:
/// writes normalize to RFC order (SQL Server order never crosses the wire), reads re-validate the
/// version-7 bits and rehydrate tagged <see cref="GuidByteOrder.Rfc9562"/>.
/// </summary>
sealed class SequentialGuidSerializer : ISerializer<SequentialGuid>, ISerializer<SequentialGuid?>
{
	public SerializerFeatures Features =>
		SerializerFeatures.WireTypeString | SerializerFeatures.CategoryScalar;

	public SequentialGuid Read(ref ProtoReader.State state, SequentialGuid value) =>
		new(GuidWire.Read(ref state), GuidByteOrder.Rfc9562);

	public void Write(ref ProtoWriter.State state, SequentialGuid value) =>
		GuidWire.Write(ref state, value.ToRfcOrder().Value);

	SequentialGuid? ISerializer<SequentialGuid?>.Read(ref ProtoReader.State state, SequentialGuid? value) =>
		Read(ref state, value.GetValueOrDefault());

	void ISerializer<SequentialGuid?>.Write(ref ProtoWriter.State state, SequentialGuid? value) =>
		Write(ref state, value.GetValueOrDefault());
}
```

In `IdentifierSerializers.cs`, add `using Norse.Primitives.Identifiers;` to the usings and extend `Register` after the `AfterApplyDefaultBehaviour` subscription:

```csharp
		model.AfterApplyDefaultBehaviour += SweepGuidMembers;
		model.Add(typeof(SequentialGuid), applyDefaultBehaviour: false).SerializerType =
			typeof(SequentialGuidSerializer);
```

- [ ] **Step 4: Run the tests to verify they pass**

```bash
dotnet test Midgard/tests/Infrastructure.Web.Grpc.Tests/Infrastructure.Web.Grpc.Tests.csproj
```

Expected: PASS, 16 tests (9 from Task 1 + 7 new).

- [ ] **Step 5: Commit**

```bash
git -C Midgard branch --show-current   # must print feature/grpc-wire-format
git -C Midgard add src/Infrastructure.Web.Grpc tests/Infrastructure.Web.Grpc.Tests
git -C Midgard commit -m "Add SequentialGuid custom scalar serializer (RFC order, normalize-on-write)"
```

---

### Task 3: `DeterministicGuidSerializer`

**Files:**
- Create: `Midgard/src/Infrastructure.Web.Grpc/DeterministicGuidSerializer.cs`
- Modify: `Midgard/src/Infrastructure.Web.Grpc/IdentifierSerializers.cs`
- Create: `Midgard/tests/Infrastructure.Web.Grpc.Tests/DeterministicGuidSerializerTests.cs`

**Interfaces:**
- Consumes: `GuidWire` (Task 2); Svartálfheim `DeterministicGuid(Guid)` (throws `ArgumentException` on non-v5 bits — verified in source), `DeterministicGuid.Value`.
- Produces: `sealed class DeterministicGuidSerializer : ISerializer<DeterministicGuid>, ISerializer<DeterministicGuid?>` registered inside `IdentifierSerializers.Register`.

- [ ] **Step 1: Write the failing tests**

`Midgard/tests/Infrastructure.Web.Grpc.Tests/DeterministicGuidSerializerTests.cs`:

```csharp
using Norse.Primitives.Identifiers;
using ProtoBuf;

namespace Norse.Infrastructure.Web.Grpc.Tests;

public sealed class DeterministicGuidSerializerTests
{
	// The RFC 9562 §A.4 example UUIDv5 (DNS namespace, "www.example.com").
	static readonly Guid KnownV5 = new("2ed6657d-e927-568b-95e1-2665a8aea6a2");
	const string KnownWireHex = "0A102ED6657DE927568B95E12665A8AEA6A2";

	[Fact]
	void Serializes_as_sixteen_rfc_9562_bytes()
	{
		var model = TestModel.Create();
		var payload = TestModel.Serialize(model, new DeterministicGuidEnvelope { Id = new(KnownV5) });
		Convert.ToHexString(payload).ShouldBe(KnownWireHex);
	}

	[Fact]
	void Round_trips_equal_to_the_original()
	{
		var model = TestModel.Create();
		DeterministicGuid original = new(KnownV5);
		var payload = TestModel.Serialize(model, new DeterministicGuidEnvelope { Id = original });
		TestModel.Deserialize<DeterministicGuidEnvelope>(model, payload).Id.ShouldBe(original);
	}

	[Fact]
	void Round_trips_a_nullable_member()
	{
		var model = TestModel.Create();
		var payload = TestModel.Serialize(model, new NullableDeterministicGuidEnvelope { Id = new(KnownV5) });
		TestModel.Deserialize<NullableDeterministicGuidEnvelope>(model, payload)
			.Id.ShouldBe(new DeterministicGuid(KnownV5));
	}

	[Fact]
	void Round_trips_a_null_nullable_member_as_null()
	{
		var model = TestModel.Create();
		var payload = TestModel.Serialize(model, new NullableDeterministicGuidEnvelope());
		TestModel.Deserialize<NullableDeterministicGuidEnvelope>(model, payload).Id.ShouldBeNull();
	}

	[Fact]
	void Throws_on_a_truncated_payload()
	{
		var model = TestModel.Create();
		byte[] truncated = [0x0A, 0x0F, .. new byte[15]];
		Should.Throw<InvalidDataException>(() =>
			TestModel.Deserialize<DeterministicGuidEnvelope>(model, truncated));
	}

	[Fact]
	void Throws_on_sixteen_bytes_that_are_not_a_version_5_uuid()
	{
		var model = TestModel.Create();
		// The §A.6 v7 example: valid UUID bits, wrong version for DeterministicGuid.
		byte[] v7Payload = [0x0A, 0x10, .. Convert.FromHexString("017F22E279B07CC398C4DC0C0C07398F")];
		Should.Throw<ArgumentException>(() =>
			TestModel.Deserialize<DeterministicGuidEnvelope>(model, v7Payload));
	}
}

[ProtoContract]
public sealed class DeterministicGuidEnvelope
{
	[ProtoMember(1)]
	public DeterministicGuid Id { get; set; }
}

[ProtoContract]
public sealed class NullableDeterministicGuidEnvelope
{
	[ProtoMember(1)]
	public DeterministicGuid? Id { get; set; }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet test Midgard/tests/Infrastructure.Web.Grpc.Tests/Infrastructure.Web.Grpc.Tests.csproj
```

Expected: the six new tests FAIL (no serializer registered for `DeterministicGuid`).

- [ ] **Step 3: Implement and register**

`Midgard/src/Infrastructure.Web.Grpc/DeterministicGuidSerializer.cs`:

```csharp
using Norse.Primitives.Identifiers;
using ProtoBuf;
using ProtoBuf.Serializers;

namespace Norse.Infrastructure.Web.Grpc;

/// <summary>
/// Puts <see cref="DeterministicGuid"/> on the wire as the canonical 16-byte RFC 9562 <c>bytes</c>
/// payload; reads re-validate the version-5 bits via the wrapping constructor and fail loudly on garbage.
/// </summary>
sealed class DeterministicGuidSerializer : ISerializer<DeterministicGuid>, ISerializer<DeterministicGuid?>
{
	public SerializerFeatures Features =>
		SerializerFeatures.WireTypeString | SerializerFeatures.CategoryScalar;

	public DeterministicGuid Read(ref ProtoReader.State state, DeterministicGuid value) =>
		new(GuidWire.Read(ref state));

	public void Write(ref ProtoWriter.State state, DeterministicGuid value) =>
		GuidWire.Write(ref state, value.Value);

	DeterministicGuid? ISerializer<DeterministicGuid?>.Read(ref ProtoReader.State state, DeterministicGuid? value) =>
		Read(ref state, value.GetValueOrDefault());

	void ISerializer<DeterministicGuid?>.Write(ref ProtoWriter.State state, DeterministicGuid? value) =>
		Write(ref state, value.GetValueOrDefault());
}
```

In `IdentifierSerializers.Register`, after the `SequentialGuid` line:

```csharp
		model.Add(typeof(DeterministicGuid), applyDefaultBehaviour: false).SerializerType =
			typeof(DeterministicGuidSerializer);
```

- [ ] **Step 4: Run the tests to verify they pass**

```bash
dotnet test Midgard/tests/Infrastructure.Web.Grpc.Tests/Infrastructure.Web.Grpc.Tests.csproj
```

Expected: PASS, 22 tests.

- [ ] **Step 5: Commit**

```bash
git -C Midgard branch --show-current   # must print feature/grpc-wire-format
git -C Midgard add src/Infrastructure.Web.Grpc tests/Infrastructure.Web.Grpc.Tests
git -C Midgard commit -m "Add DeterministicGuid custom scalar serializer"
```

---

### Task 4: Generator emission + Web.Client/Web.Server adoption

**Files:**
- Modify: `Midgard/gen/Infrastructure.Web.Client.Generator/ClientRegistrationEmitter.cs`
- Modify: `Midgard/gen/Infrastructure.Web.Server.Generator/ServerRegistrationEmitter.cs`
- Modify: `Midgard/src/Infrastructure.Web.Client/Infrastructure.Web.Client.csproj`
- Modify: `Midgard/src/Infrastructure.Web.Server/Infrastructure.Web.Server.csproj`
- Modify: `Midgard/tests/Infrastructure.Web.Client.Generator.Tests/GrpcClientRegistrationGeneratorTests.cs`
- Modify: `Midgard/tests/Infrastructure.Web.Server.Generator.Tests/GrpcServerRegistrationGeneratorTests.cs`

**Interfaces:**
- Consumes: `IdentifierSerializers.Register(RuntimeTypeModel)` (Task 1); the existing `Generate(...)` helpers in both generator test suites.
- Produces: both generated `RegisterNorseOutcomeSurrogates()` bodies call `global::Norse.Infrastructure.Web.Grpc.IdentifierSerializers.Register(model);` before any `Outcome<T>` surrogate is added (the spec §4 ordering constraint: wire law lands before contract types enter the model).

- [ ] **Step 1: Write the failing generator tests**

Append to `GrpcClientRegistrationGeneratorTests` (inside the class, matching the file's existing index-comparison style):

```csharp
	[Fact]
	void Registers_the_identifier_serializers_before_the_Outcome_surrogates()
	{
		var generated = Generate(Contract);
		var registerIndex = generated.IndexOf(
			"global::Norse.Infrastructure.Web.Grpc.IdentifierSerializers.Register(model);",
			StringComparison.Ordinal);
		var surrogateIndex = generated.IndexOf(".SetSurrogate(", StringComparison.Ordinal);
		registerIndex.ShouldBeGreaterThan(-1);
		registerIndex.ShouldBeLessThan(surrogateIndex);
	}
```

Append the identical test to `GrpcServerRegistrationGeneratorTests` (same body — its `Generate` helper is that suite's own).

- [ ] **Step 2: Run both generator test suites to verify the new tests fail**

```bash
dotnet test Midgard/tests/Infrastructure.Web.Client.Generator.Tests/Infrastructure.Web.Client.Generator.Tests.csproj
dotnet test Midgard/tests/Infrastructure.Web.Server.Generator.Tests/Infrastructure.Web.Server.Generator.Tests.csproj
```

Expected: exactly one FAIL per suite (`registerIndex` is −1).

- [ ] **Step 3: Add the emitted line to both emitters**

In `ClientRegistrationEmitter.cs` **and** `ServerRegistrationEmitter.cs`, inside the raw string template, directly after the line

```
			var model = global::ProtoBuf.Meta.RuntimeTypeModel.Default;
```

add (same template indentation as the `var model` line):

```
			global::Norse.Infrastructure.Web.Grpc.IdentifierSerializers.Register(model);
```

- [ ] **Step 4: Add the ProjectReference both runtime assemblies need for consumers to resolve the emitted call**

In `Infrastructure.Web.Client.csproj` and `Infrastructure.Web.Server.csproj`, add to the existing `<ItemGroup>` (after the existing `<ProjectReference>` to the generator):

```xml
		<ProjectReference Include="../Infrastructure.Web.Grpc/Infrastructure.Web.Grpc.csproj" />
```

In the same pass, update each `Description`: in both files replace the trailing text `plus idempotent Outcome&lt;T&gt; surrogate wiring.` with `plus idempotent Outcome&lt;T&gt; surrogate wiring and the Norse.Infrastructure.Web.Grpc identifier wire law (CompatibilityLevel 300, 16-byte RFC 9562 identifier bytes fields).`

- [ ] **Step 5: Run the full Midgard suite to verify everything passes**

```bash
dotnet build Midgard/Midgard.slnx
dotnet test Midgard/Midgard.slnx
```

Expected: build clean (zero warnings), all tests PASS including both new generator tests.

- [ ] **Step 6: Commit**

```bash
git -C Midgard branch --show-current   # must print feature/grpc-wire-format
git -C Midgard add gen src tests
git -C Midgard commit -m "Emit IdentifierSerializers.Register ahead of Outcome surrogates in both wiring generators"
```

---

### Task 5: Yggdrasil — code-first gRPC reflection

**Files:**
- Modify: `Yggdrasil/Directory.Packages.props`
- Modify: `Yggdrasil/src/Hosting.Web.Server/Hosting.Web.Server.csproj`
- Modify: `Yggdrasil/src/Hosting.Web.Server/Program.cs`

**Interfaces:**
- Consumes: `protobuf-net.Grpc.AspNetCore.Reflection` 1.2.2 — `AddCodeFirstGrpcReflection()` on `IServiceCollection` and `MapCodeFirstGrpcReflectionService()` on `IEndpointRouteBuilder`, both in namespace `ProtoBuf.Grpc.Server` (verified against package source).
- Produces: the dev-gated reflection endpoint serves the **code-first** service catalog (`grpcurl` can list and describe `IAuthenticationService`).

- [ ] **Step 1: Create the Yggdrasil feature branch**

```bash
git -C Yggdrasil checkout -b feature/code-first-reflection
```

- [ ] **Step 2: Swap the package pin and add the census pin**

In `Yggdrasil/Directory.Packages.props`:

1. Delete the line `<PackageVersion Include="Grpc.AspNetCore.Server.Reflection" Version="$(GrpcVersion)" />` (nothing references the stock middleware after this task).
2. Add, alphabetically after the `protobuf-net.Grpc` pin: `<PackageVersion Include="protobuf-net.Grpc.AspNetCore.Reflection" Version="1.2.2" />`
3. Census law (every Norse package is pinned platform-wide, even before first direct use): add `<PackageVersion Include="Norse.Infrastructure.Web.Grpc" Version="..." />` alphabetically into the Norse pin block, copying the `Version` value verbatim from the existing `Norse.Infrastructure.Web.Client` pin line — the two ship on the same Midgard release train.

(Dependency-floor note: the new package floors `Grpc.AspNetCore.Server >= 2.66.0` — satisfied by the pinned `$(GrpcVersion)` 2.80.0 — and drags protobuf-net only through stale floors, which the existing exact `protobuf-net` 3.2.56 pin plus the direct reference in `Hosting.Web.Server.csproj` already cure. No further pin work needed.)

- [ ] **Step 3: Swap the package reference and calls**

In `Yggdrasil/src/Hosting.Web.Server/Hosting.Web.Server.csproj`, replace `<PackageReference Include="Grpc.AspNetCore.Server.Reflection" />` with `<PackageReference Include="protobuf-net.Grpc.AspNetCore.Reflection" />`, repositioned so the ItemGroup stays alphabetical (it moves next to the existing `protobuf-net` reference).

In `Yggdrasil/src/Hosting.Web.Server/Program.cs`:

1. Ensure `using ProtoBuf.Grpc.Server;` is present in the using block (hoisted, alphabetical; it may already be there for `AddNorseCodeFirstGrpc`'s underlying types — check before adding).
2. In the service-registration fluent chain, replace `.AddGrpcReflection();` with `.AddCodeFirstGrpcReflection();` — the existing three-line rationale comment above it stays exactly as written (it becomes *more* true: the stock middleware never actually served the code-first catalog).
3. Inside the `if (app.Environment.IsDevelopment())` block, replace `app.MapGrpcReflectionService();` with `app.MapCodeFirstGrpcReflectionService();`.

- [ ] **Step 4: Build and test Yggdrasil**

```bash
dotnet build Yggdrasil/Yggdrasil.slnx
dotnet test Yggdrasil/Yggdrasil.slnx
```

Expected: build clean, all tests PASS. (No existing test asserts the stock reflection registration — verified by grep before this plan was written.)

- [ ] **Step 5: Commit**

```bash
git -C Yggdrasil branch --show-current   # must print feature/code-first-reflection
git -C Yggdrasil add Directory.Packages.props src/Hosting.Web.Server
git -C Yggdrasil commit -m "Swap stock gRPC reflection for protobuf-net.Grpc code-first reflection"
```

---

### Task 6: Live verification, spec amendment, docs sync

**Files:**
- Modify: `Glitnir/docs/Platform/specs/2026-07-27-grpc-wire-format-identifier-serializers-design.md` (verification amendment — stage only)
- Modify: `Midgard/CLAUDE.md`, `Midgard/README.md` (boy-scout sync — commit on the Midgard feature branch)
- Modify: `Bifrost.slnx` (stage only, on `master`, never commit)

**Interfaces:**
- Consumes: everything shipped in Tasks 1–5; the Bifröst AppHost (`src/Orchestration.AppHost`); `grpcurl` (install if missing: `go install github.com/fullstorydev/grpcurl/cmd/grpcurl@latest` — the Go toolchain is present per TOOLCHAIN.md).
- Produces: the recorded resolution of the spec's §5.1(3) named obligation (what reflection serves for custom-serialized members), filed as a spec amendment.

- [ ] **Step 1: Add the new Midgard projects to `Bifrost.slnx`**

In the Midgard solution folders of `Bifrost.slnx`, mirror the Task 1 entries: `<Project Path="Midgard/src/Infrastructure.Web.Grpc/Infrastructure.Web.Grpc.csproj" />` after the `Infrastructure.Web.Client` entry, and `<Project Path="Midgard/tests/Infrastructure.Web.Grpc.Tests/Infrastructure.Web.Grpc.Tests.csproj" />` after the `Infrastructure.Web.Client.Tests` entry. Confirm `git -C . branch --show-current` prints `master`, then `git add Bifrost.slnx` — **stage only, never commit Bifröst.**

- [ ] **Step 2: Run the composition live**

Docker must be running. Then:

```bash
dotnet run --project src/Orchestration.AppHost
```

(run in the background; wait for the migrations service to complete and the web server resource to report Running; note the web server's HTTPS endpoint from the console/dashboard output — call it `$SERVER` below).

- [ ] **Step 3: Prove the reflection surface with grpcurl**

```bash
grpcurl -insecure $SERVER list
grpcurl -insecure $SERVER describe        # full catalog
```

Expected: the service list includes the Norse code-first contract (e.g. `Norse.AuthN.Services.IAuthenticationService`) — the stock middleware showed only `grpc.reflection.v1alpha.ServerReflection`. Then describe the request/response messages and **record exactly how identifier-typed members render** (any `Guid` member should show as `bytes`; if any contract member uses `SequentialGuid`/`DeterministicGuid`, record what the descriptor emits for it — this is the §5.1(3) named obligation). Capture the relevant output verbatim. Stop the AppHost when done.

- [ ] **Step 4: File the verification amendment**

Append to the spec (`Glitnir/docs/Platform/specs/2026-07-27-grpc-wire-format-identifier-serializers-design.md`) a new subsection `### 6.1 Live verification` (dated the day the verification runs) under §6 containing: the grpcurl service list, how identifier members rendered in the described messages, and the resolution (or escalation) of the §5.1(3) schema-rendering obligation — if custom-serialized members render as a dangling/invalid type in the served descriptors, say so plainly and mark it as an open defect for a follow-up ruling; do not paper over it. Stage in Glitnir (`git -C Glitnir add docs/Platform/specs/...`) — never commit.

- [ ] **Step 5: Boy-scout the Midgard doc pair**

On the Midgard feature branch: in `Midgard/CLAUDE.md` §1, extend the live-slices sentence to include `Infrastructure.Web.Grpc` (the shared wire-law project: `IdentifierSerializers`, CompatibilityLevel 300, 16-byte RFC 9562 identifier bytes, referenced by both `Web.Client` and `Web.Server`, and Midgard's one edge to Svartálfheim's `Primitives`). Update `Midgard/README.md` wherever it enumerates projects to match. Commit on the feature branch:

```bash
git -C Midgard branch --show-current   # must print feature/grpc-wire-format
git -C Midgard add CLAUDE.md README.md
git -C Midgard commit -m "Document Infrastructure.Web.Grpc in the realm doc pair"
```

- [ ] **Step 6: Final full verification**

```bash
dotnet build Midgard/Midgard.slnx && dotnet test Midgard/Midgard.slnx
dotnet build Yggdrasil/Yggdrasil.slnx && dotnet test Yggdrasil/Yggdrasil.slnx
```

Expected: everything green. Report done; Buvy drives the PR/tag/publish ceremony (Midgard first, then the Yggdrasil pin-bump PR).
