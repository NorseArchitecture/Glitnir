# .editorconfig Curation (Phase 2) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Land the full style law from `docs/superpowers/specs/2026-06-05-editorconfig-curation-design.md` in the `poc/build` replica, prove it with harness canaries, and produce a `.slnx` Buvy can open in Visual Studio for the muscle-memory test drive.

**Architecture:** Same proven pattern as Phase 1 — everything lands in the `poc/build` replica first; real-tree seeding is a later, separate pass. The root `.editorconfig` replaces the three-line seed with the complete declared law. New style canaries ride the existing `EnableCanaries` toggle (via conditional `<Compile Remove>`, not `#if`, to dodge disabled-text ambiguity). A Razor class library probe joins `src/` for the Ctrl+K,D acceptance check. The harness grows IDE canaries, an inverse canary, and landing assertions.

**Tech Stack:** .NET 11 preview SDK (via repo `global.json`), MSBuild, Roslyn style analyzers, PowerShell 7 harness, `Microsoft.NET.Sdk.Razor`.

**House override on commits (CLAUDE.md §8):** No automatic git commits — every "checkpoint" step means *stage, show the diff, stop*. Buvy commits. Suggested messages are provided but he runs them.

---

## File Map

| File | Action | Task |
|---|---|---|
| `poc/build/.editorconfig` | Replace seed with full law | 1 |
| `poc/build/src/Directory.Build.props` | Add `JsonSerializerIsReflectionEnabledByDefault=false` | 2 |
| `poc/build/Verify-Enforcement.ps1` | Landing assertions, style-canary IDs, `'!~*'` hardening | 2, 3, 6 |
| `poc/build/src/Glitnir.Probe/StyleCanaries.cs` | Create — style/naming/logging canaries | 4 |
| `poc/build/src/Glitnir.Probe/Glitnir.Probe.csproj` | `<Compile Remove>` gating + logging package | 4 |
| `poc/build/src/Glitnir.Probe/JudgmentCitizen.cs` | Create — inverse canary (always compiled) | 5 |
| `poc/build/src/Glitnir.Probe.Components/` | Create — Razor probe (csproj + `TabProbe.razor`) | 7 |
| `poc/build/Glitnir.BuildLaw.slnx` | Create — VS test-drive solution | 8 |
| `poc/build/README.md` | Test-drive checklist section | 9 |
| `poc/build/FINDINGS.md` | Phase 2 appendix | 9 |
| `CLAUDE.md` §4, §8 | Amendments per spec §10 | 10 |
| `docs/conventions.md` | Async elide law + tuple idiom | 10 |
| `docs/spec-reconciliation-2026-06-04.md` | Check off 4.2 Phase 2 mechanics | 10 |
| `docs/superpowers/specs/2026-06-05-build-enforcement-design.md` | §5 `src/` delta gains JSON switch | 10 |

---

### Task 1: The Full Root `.editorconfig` (Replica)

**Files:**
- Modify: `poc/build/.editorconfig` (replace entire content)

- [ ] **Step 1: Confirm the current harness baseline is green**

Run: `pwsh poc/build/Verify-Enforcement.ps1`
Expected: exit 0, "All enforcement assertions passed." (If not green before we start, stop — Phase 1 substrate regressed; investigate before layering Phase 2.)

- [ ] **Step 2: Replace `poc/build/.editorconfig` with the full law**

The complete file. Tier convention: error tier is unmarked; every silent entry carries `# judgment:`. Form rules: bare option values, separate `dotnet_diagnostic` severities (spec §3).

```ini
# Root style law — Phase 2 of build enforcement.
# Spec: docs/superpowers/specs/2026-06-05-editorconfig-curation-design.md
# Tier convention: error tier is unmarked; silent tier carries "# judgment:" with its reason.
# Frozen-at-stock: options with no session ruling are declared at Roslyn's stock value to freeze them.
root = true

###############################################################################
# 1. Universal defaults — mirrors .gitattributes (eol=lf law, 2026-06-05)
###############################################################################
[*]
charset = utf-8
end_of_line = lf
insert_final_newline = true
trim_trailing_whitespace = true
indent_style = tab
tab_width = 4
indent_size = 4

###############################################################################
# 2. Declared exceptions — don't fight the ecosystem (codified law)
###############################################################################
[*.{yml,yaml}]
# whitespace-aware language; ecosystem norm is 2
indent_style = space
indent_size = 2

[*.md]
# trailing double-space is a hard line break
indent_style = space
indent_size = 2
trim_trailing_whitespace = false

[*.json]
# .NET tooling rewrites JSON 2-space; fighting it churns diffs
indent_style = space
indent_size = 2

[*.py]
# PEP 8; Black is hardcoded to 4
indent_style = space
indent_size = 4

[*.{fs,fsx,fsi}]
# F# compiler rejects tabs; style guide / Fantomas is 4
indent_style = space
indent_size = 4

[*.{bat,cmd}]
# cmd.exe requires CRLF; matches .gitattributes
end_of_line = crlf

[*.{razor,cshtml}]
# explicit section, never [*] fallthrough — legacy VS Razor formatter only
# respected explicit razor sections; see spec §4 for the Ctrl+K,D history
indent_style = tab
tab_width = 4
indent_size = 4

###############################################################################
# 3. C# style law
###############################################################################
[*.cs]

## ── Usings ──────────────────────────────────────────────────────────────
dotnet_sort_system_directives_first = true
dotnet_separate_import_directive_groups = false
csharp_using_directive_placement = outside_namespace
dotnet_diagnostic.IDE0005.severity = error
dotnet_diagnostic.IDE0065.severity = error

## ── Namespaces ──────────────────────────────────────────────────────────
csharp_style_namespace_declarations = file_scoped
dotnet_style_namespace_match_folder = true
dotnet_diagnostic.IDE0161.severity = error
dotnet_diagnostic.IDE0130.severity = error

## ── Accessibility — omission is the default (CLAUDE.md §2.3) ───────────
dotnet_style_require_accessibility_modifiers = omit_if_default
dotnet_diagnostic.IDE0040.severity = error

## ── The var law: var everywhere, except construction — type left, new() right
csharp_style_var_for_built_in_types = true
csharp_style_var_when_type_is_apparent = false
csharp_style_var_elsewhere = true
csharp_style_implicit_object_creation_when_type_is_apparent = true
dotnet_diagnostic.IDE0007.severity = error
dotnet_diagnostic.IDE0008.severity = error
dotnet_diagnostic.IDE0090.severity = error

## ── Collection expressions — modern idiom enforced ─────────────────────
dotnet_style_prefer_collection_expression = when_types_loosely_match
dotnet_diagnostic.IDE0300.severity = error
dotnet_diagnostic.IDE0301.severity = error
dotnet_diagnostic.IDE0302.severity = error
dotnet_diagnostic.IDE0303.severity = error
dotnet_diagnostic.IDE0304.severity = error
dotnet_diagnostic.IDE0305.severity = error
dotnet_diagnostic.IDE0306.severity = error

## ── Bodies and braces ───────────────────────────────────────────────────
csharp_prefer_braces = true
dotnet_diagnostic.IDE0011.severity = error
csharp_preferred_modifier_order = public,private,protected,internal,file,static,extern,new,virtual,abstract,sealed,override,readonly,unsafe,required,volatile,async
dotnet_diagnostic.IDE0036.severity = error
dotnet_style_readonly_field = true
dotnet_diagnostic.IDE0044.severity = error
csharp_style_expression_bodied_properties = true
csharp_style_expression_bodied_indexers = true
csharp_style_expression_bodied_accessors = true
dotnet_diagnostic.IDE0025.severity = error
dotnet_diagnostic.IDE0026.severity = error
dotnet_diagnostic.IDE0027.severity = error
# judgment: right ~80% — the rule that bit the POC under stock defaults; not worth policing the rest
csharp_style_expression_bodied_methods = when_on_single_line
csharp_style_expression_bodied_constructors = when_on_single_line
csharp_style_expression_bodied_operators = when_on_single_line
csharp_style_expression_bodied_local_functions = when_on_single_line
csharp_style_expression_bodied_lambdas = true
dotnet_diagnostic.IDE0021.severity = silent
dotnet_diagnostic.IDE0022.severity = silent
dotnet_diagnostic.IDE0023.severity = silent
dotnet_diagnostic.IDE0024.severity = silent
dotnet_diagnostic.IDE0061.severity = silent

## ── Null handling ───────────────────────────────────────────────────────
dotnet_style_coalesce_expression = true
dotnet_style_null_propagation = true
dotnet_style_prefer_is_null_check_over_reference_equality_method = true
csharp_style_throw_expression = true
csharp_style_conditional_delegate_call = true
csharp_style_prefer_null_check_over_type_check = true
dotnet_diagnostic.IDE0029.severity = error
dotnet_diagnostic.IDE0030.severity = error
dotnet_diagnostic.IDE0031.severity = error
dotnet_diagnostic.IDE0041.severity = error
dotnet_diagnostic.IDE0016.severity = error

## ── Keywords and simplification ─────────────────────────────────────────
dotnet_style_predefined_type_for_locals_parameters_members = true
dotnet_style_predefined_type_for_member_access = true
dotnet_diagnostic.IDE0049.severity = error
csharp_prefer_simple_default_expression = true
dotnet_diagnostic.IDE0034.severity = error
dotnet_style_prefer_inferred_tuple_names = true
dotnet_style_prefer_inferred_anonymous_type_member_names = true
dotnet_diagnostic.IDE0037.severity = error
csharp_style_prefer_method_group_conversion = true

## ── Hygiene ─────────────────────────────────────────────────────────────
dotnet_code_quality_unused_parameters = all
dotnet_diagnostic.IDE0060.severity = error
csharp_style_unused_value_assignment_preference = discard_variable
dotnet_diagnostic.IDE0059.severity = error
# judgment: fluent APIs make unconsumed expression values routine; policing this fights the platform's own idioms
csharp_style_unused_value_expression_statement_preference = discard_variable
dotnet_diagnostic.IDE0058.severity = silent
dotnet_style_qualification_for_field = false
dotnet_style_qualification_for_property = false
dotnet_style_qualification_for_method = false
dotnet_style_qualification_for_event = false
dotnet_diagnostic.IDE0003.severity = error

## ── Frozen-at-stock declarations (no session ruling; declared to freeze) ─
dotnet_style_object_initializer = true
dotnet_style_collection_initializer = true
dotnet_style_explicit_tuple_names = true
dotnet_style_prefer_auto_properties = true
dotnet_style_prefer_compound_assignment = true
dotnet_style_prefer_simplified_boolean_expressions = true
dotnet_style_prefer_simplified_interpolation = true
csharp_style_prefer_not_pattern = true
csharp_style_inlined_variable_declaration = true
csharp_style_deconstructed_variable_declaration = true
csharp_prefer_simple_using_statement = true
csharp_style_prefer_utf8_string_literals = true
csharp_style_prefer_readonly_struct = true
csharp_style_prefer_readonly_struct_member = true

## ── Silent tier — judgment rules, declared values, IDE nudge only ──────
# judgment: the famous readability degrader — right often, wrong badly in nontrivial cases
dotnet_style_prefer_conditional_expression_over_assignment = true
dotnet_style_prefer_conditional_expression_over_return = true
dotnet_diagnostic.IDE0045.severity = silent
dotnet_diagnostic.IDE0046.severity = silent
# judgment: complex bodies read worse as switch expressions
csharp_style_prefer_switch_expression = true
dotnet_diagnostic.IDE0066.severity = silent
# judgment: capture semantics make primary constructors genuinely situational
csharp_style_prefer_primary_constructors = true
dotnet_diagnostic.IDE0290.severity = silent
# judgment: preferred idiom, not a correctness issue
csharp_style_pattern_matching_over_as_with_null_check = true
csharp_style_pattern_matching_over_is_with_cast_check = true
csharp_style_prefer_pattern_matching = true
dotnet_diagnostic.IDE0019.severity = silent
dotnet_diagnostic.IDE0020.severity = silent
dotnet_diagnostic.IDE0038.severity = silent
dotnet_diagnostic.IDE0078.severity = silent
# judgment: clarity is the point; enforcement would litigate taste
dotnet_style_parentheses_in_arithmetic_binary_operators = always_for_clarity
dotnet_style_parentheses_in_relational_binary_operators = always_for_clarity
dotnet_style_parentheses_in_other_binary_operators = always_for_clarity
dotnet_style_parentheses_in_other_operators = never_if_unnecessary
dotnet_diagnostic.IDE0047.severity = silent
dotnet_diagnostic.IDE0048.severity = silent
# judgment: situational
csharp_style_prefer_local_over_anonymous_function = true
dotnet_diagnostic.IDE0039.severity = silent
csharp_style_prefer_index_operator = true
csharp_style_prefer_range_operator = true
dotnet_diagnostic.IDE0056.severity = silent
dotnet_diagnostic.IDE0057.severity = silent
# judgment: hosts use them; libraries have no entry points to care
csharp_style_prefer_top_level_statements = true
dotnet_diagnostic.IDE0210.severity = silent

## ── Targeted CA severities — rules unreachable by the props category knobs
# CA1727 is Naming category (not escalated, disabled by default): PascalCase log placeholders
dotnet_diagnostic.CA1727.severity = error

###############################################################################
# 4. C# formatting law — stock VS conventions, declared explicitly, on tabs
###############################################################################
dotnet_diagnostic.IDE0055.severity = error

csharp_new_line_before_open_brace = all
csharp_new_line_before_else = true
csharp_new_line_before_catch = true
csharp_new_line_before_finally = true
csharp_new_line_before_members_in_object_initializers = true
csharp_new_line_before_members_in_anonymous_types = true
csharp_new_line_between_query_expression_clauses = true

csharp_indent_case_contents = true
csharp_indent_switch_labels = true
csharp_indent_labels = one_less_than_current
csharp_indent_block_contents = true
csharp_indent_braces = false
csharp_indent_case_contents_when_block = false

csharp_space_after_cast = false
csharp_space_after_keywords_in_control_flow_statements = true
csharp_space_between_parentheses = false
csharp_space_before_colon_in_inheritance_clause = true
csharp_space_after_colon_in_inheritance_clause = true
csharp_space_around_binary_operators = before_and_after
csharp_space_between_method_declaration_parameter_list_parentheses = false
csharp_space_between_method_declaration_empty_parameter_list_parentheses = false
csharp_space_between_method_declaration_name_and_open_parenthesis = false
csharp_space_between_method_call_parameter_list_parentheses = false
csharp_space_between_method_call_empty_parameter_list_parentheses = false
csharp_space_between_method_call_name_and_opening_parenthesis = false
csharp_space_after_comma = true
csharp_space_before_comma = false
csharp_space_after_dot = false
csharp_space_before_dot = false
csharp_space_after_semicolon_in_for_statement = true
csharp_space_before_semicolon_in_for_statement = false
csharp_space_around_declaration_statements = false
csharp_space_before_open_square_brackets = false
csharp_space_between_empty_square_brackets = false
csharp_space_between_square_brackets = false

csharp_preserve_single_line_statements = false
csharp_preserve_single_line_blocks = true

###############################################################################
# 5. Naming law — classic C#, nothing custom, all error.
# Rule order matters: first match wins — constants precede the field rules.
# Async suffix deliberately omitted: interface-driven surfaces (NSB Handle,
# mediator Handle) own their names; a hard suffix rule fights the ecosystem.
###############################################################################

## Styles
dotnet_naming_style.pascal_case.capitalization = pascal_case
dotnet_naming_style.camel_case.capitalization = camel_case
dotnet_naming_style.interface_prefixed.required_prefix = I
dotnet_naming_style.interface_prefixed.capitalization = pascal_case
dotnet_naming_style.type_parameter_prefixed.required_prefix = T
dotnet_naming_style.type_parameter_prefixed.capitalization = pascal_case
dotnet_naming_style.underscore_camel_case.required_prefix = _
dotnet_naming_style.underscore_camel_case.capitalization = camel_case

## Symbols
dotnet_naming_symbols.interfaces.applicable_kinds = interface
dotnet_naming_symbols.interfaces.applicable_accessibilities = *
dotnet_naming_symbols.type_parameters.applicable_kinds = type_parameter
dotnet_naming_symbols.type_parameters.applicable_accessibilities = *
dotnet_naming_symbols.types.applicable_kinds = class, struct, enum, delegate
dotnet_naming_symbols.types.applicable_accessibilities = *
dotnet_naming_symbols.members.applicable_kinds = property, method, event, local_function
dotnet_naming_symbols.members.applicable_accessibilities = *
dotnet_naming_symbols.constants.applicable_kinds = field, local
dotnet_naming_symbols.constants.applicable_accessibilities = *
dotnet_naming_symbols.constants.required_modifiers = const
dotnet_naming_symbols.private_fields.applicable_kinds = field
dotnet_naming_symbols.private_fields.applicable_accessibilities = private, private_protected
dotnet_naming_symbols.visible_fields.applicable_kinds = field
dotnet_naming_symbols.visible_fields.applicable_accessibilities = public, internal, protected, protected_internal
dotnet_naming_symbols.parameters.applicable_kinds = parameter
dotnet_naming_symbols.parameters.applicable_accessibilities = *
dotnet_naming_symbols.locals.applicable_kinds = local
dotnet_naming_symbols.locals.applicable_accessibilities = *

## Rules (ordered — first match wins)
dotnet_naming_rule.interfaces_are_prefixed.symbols = interfaces
dotnet_naming_rule.interfaces_are_prefixed.style = interface_prefixed
dotnet_naming_rule.interfaces_are_prefixed.severity = error
dotnet_naming_rule.type_parameters_are_prefixed.symbols = type_parameters
dotnet_naming_rule.type_parameters_are_prefixed.style = type_parameter_prefixed
dotnet_naming_rule.type_parameters_are_prefixed.severity = error
dotnet_naming_rule.types_are_pascal_case.symbols = types
dotnet_naming_rule.types_are_pascal_case.style = pascal_case
dotnet_naming_rule.types_are_pascal_case.severity = error
dotnet_naming_rule.members_are_pascal_case.symbols = members
dotnet_naming_rule.members_are_pascal_case.style = pascal_case
dotnet_naming_rule.members_are_pascal_case.severity = error
dotnet_naming_rule.constants_are_pascal_case.symbols = constants
dotnet_naming_rule.constants_are_pascal_case.style = pascal_case
dotnet_naming_rule.constants_are_pascal_case.severity = error
dotnet_naming_rule.private_fields_are_underscore_camel_case.symbols = private_fields
dotnet_naming_rule.private_fields_are_underscore_camel_case.style = underscore_camel_case
dotnet_naming_rule.private_fields_are_underscore_camel_case.severity = error
dotnet_naming_rule.visible_fields_are_pascal_case.symbols = visible_fields
dotnet_naming_rule.visible_fields_are_pascal_case.style = pascal_case
dotnet_naming_rule.visible_fields_are_pascal_case.severity = error
dotnet_naming_rule.parameters_are_camel_case.symbols = parameters
dotnet_naming_rule.parameters_are_camel_case.style = camel_case
dotnet_naming_rule.parameters_are_camel_case.severity = error
dotnet_naming_rule.locals_are_camel_case.symbols = locals
dotnet_naming_rule.locals_are_camel_case.style = camel_case
dotnet_naming_rule.locals_are_camel_case.severity = error
```

- [ ] **Step 3: Verify the law tolerates lawful code**

Run: `pwsh poc/build/Verify-Enforcement.ps1`
Expected: exit 0. The existing probes are tab-indented, file-scoped, documented — they should pass unchanged. If any clean build now fails, the failure is evidence: either the probe code violates the new law (fix the code — the law is right) or the law misfires on lawful code (record in FINDINGS, adjust the option — that is what the replica is for). Do not weaken severities to get to green without recording why.

- [ ] **Step 4: Checkpoint — stage and show diff**

```
git add poc/build/.editorconfig
git diff --cached --stat
```
Suggested commit (Buvy runs it): `Land full .editorconfig style law in poc/build replica`

---

### Task 2: `src/`-Only JSON Feature Switch + Landing Assertions (TDD)

**Files:**
- Modify: `poc/build/Verify-Enforcement.ps1` (assertions first)
- Modify: `poc/build/src/Directory.Build.props`

- [ ] **Step 1: Write the failing assertions**

In `Verify-Enforcement.ps1`, the `src/` section's `Assert-Properties` call becomes:

```powershell
Assert-Properties 'src/Glitnir.Probe/Glitnir.Probe.csproj' ($law + @{ JsonSerializerIsReflectionEnabledByDefault = 'false' })
```

And the `tests/` section's call gains the inverse (the switch must NOT escape `src/`):

```powershell
Assert-Properties 'tests/Glitnir.Probe.Tests/Glitnir.Probe.Tests.csproj' ($law + @{ NoWarn = '~CS1591'; IsPackable = 'false'; JsonSerializerIsReflectionEnabledByDefault = '' })
```

- [ ] **Step 2: Run to verify failure**

Run: `pwsh poc/build/Verify-Enforcement.ps1`
Expected: FAIL — `src/...  JsonSerializerIsReflectionEnabledByDefault expected 'false', got ''`

- [ ] **Step 3: Add the delta**

`poc/build/src/Directory.Build.props` becomes:

```xml
<Project>
	<Import Project="$([MSBuild]::GetPathOfFileAbove('Directory.Build.props', '$(MSBuildThisFileDirectory)..'))" />

	<PropertyGroup>
		<!-- src/-only by ruling: tests and benchmarks reflect-serialize freely;
		     only working software is held to the source-gen posture. -->
		<JsonSerializerIsReflectionEnabledByDefault>false</JsonSerializerIsReflectionEnabledByDefault>
	</PropertyGroup>

	<ItemGroup>
		<InternalsVisibleTo Include="$(AssemblyName).Tests" />
	</ItemGroup>
</Project>
```

- [ ] **Step 4: Run to verify pass**

Run: `pwsh poc/build/Verify-Enforcement.ps1`
Expected: exit 0, both new landing checks green.

- [ ] **Step 5: Checkpoint — stage and show diff**

```
git add poc/build/src/Directory.Build.props poc/build/Verify-Enforcement.ps1
```
Suggested commit: `Add src-only JSON source-gen switch with landing assertions`

---

### Task 3: Style-Canary Expectations in the Harness (Red)

**Files:**
- Modify: `poc/build/Verify-Enforcement.ps1`

- [ ] **Step 1: Extend the `src/` canary ID list**

The `src/` `Assert-CanaryBuild` call becomes (one build, every law cluster asserted by ID):

```powershell
Assert-CanaryBuild 'src/Glitnir.Probe/Glitnir.Probe.csproj' @(
	# Phase 1 set — unchanged
	'CA5394', 'CA1810', 'CA2007', 'CA2201', 'CA2200', 'CS0219', 'CS8618',
	# Phase 2 style law
	'IDE0161',  # block-scoped namespace
	'IDE0055',  # space-indented line (formatting law)
	'IDE0007',  # explicit type where var is law
	'IDE0008',  # var on construction
	'IDE0090',  # new T() where new() is law
	'IDE0040',  # redundant accessibility modifier
	'IDE1006',  # naming law (m_-prefixed field)
	'IDE0005',  # gratuitous using — the drive-by ratchet
	'IDE0305',  # fluent .ToList() with explicit collection target
	# Phase 2 CA reach-proofs
	'CA1727',   # lowercase log placeholder (targeted editorconfig severity)
	'CA1848',   # LoggerMessage delegates (proves Performance latest-All reaches it — no editorconfig line)
	'CA2254',   # interpolated log template (proves Usage latest-All reaches it — no editorconfig line)
	'CA1852'    # unsealed internal type (proves Performance latest-All reaches it — no editorconfig line)
)
```

- [ ] **Step 2: Run to verify failure**

Run: `pwsh poc/build/Verify-Enforcement.ps1`
Expected: FAIL — every new ID reports "did not fire" (the canaries don't exist yet). The Phase 1 IDs still fire.

---

### Task 4: The Style Canaries (Green)

**Files:**
- Create: `poc/build/src/Glitnir.Probe/StyleCanaries.cs`
- Modify: `poc/build/src/Glitnir.Probe/Glitnir.Probe.csproj`

- [ ] **Step 1: Gate the new file by compilation inclusion, not `#if`**

Style and formatting diagnostics on disabled (`#if`'d-out) text are unreliable — gate the whole file instead. `Glitnir.Probe.csproj` becomes:

```xml
<Project Sdk="Microsoft.NET.Sdk">

	<PropertyGroup>
		<DefineConstants Condition="'$(EnableCanaries)' == 'true'">$(DefineConstants);CANARY</DefineConstants>
		<DefineConstants Condition="'$(EnableDocCanaries)' == 'true'">$(DefineConstants);DOC_CANARY</DefineConstants>
	</PropertyGroup>

	<ItemGroup Condition="'$(EnableCanaries)' != 'true'">
		<!-- Style canaries can't hide behind #if: formatting/style analysis of disabled
		     text is unreliable. The file leaves the compilation entirely instead. -->
		<Compile Remove="StyleCanaries.cs" />
	</ItemGroup>

	<ItemGroup>
		<PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="10.0.0" />
	</ItemGroup>

</Project>
```

(Disposable version pin per the Phase 1 pattern — CPM is its own session. If 10.0.0 doesn't restore on the preview SDK, take the latest stable 10.0.x and record it.)

- [ ] **Step 2: Write `StyleCanaries.cs`**

One violation per law cluster. Deliberately block-scoped, with one gratuitous using. Everything `internal` (no doc requirement; API-design CAs skip non-public).

```csharp
using System.Text;
using Microsoft.Extensions.Logging;

namespace Glitnir.Probe
{
	/// <summary>One member per style rule the law must catch. In the compilation only when EnableCanaries=true.</summary>
	internal static class StyleCanaryNest
	{
		/// <summary>IDE0007 — explicit built-in type where var is law.</summary>
		internal static int ExplicitWhereVarIsLaw()
		{
			int count = CountThings();
			return count;
		}

		/// <summary>IDE0008 — var on construction; the law wants the type on the left.</summary>
		internal static LawfulCitizen VarOnConstruction()
		{
			var citizen = new LawfulCitizen();
			return citizen;
		}

		/// <summary>IDE0090 — new T() where target-typed new() is law.</summary>
		internal static LawfulCitizen VerboseNew()
		{
			LawfulCitizen citizen = new LawfulCitizen();
			return citizen;
		}

		/// <summary>IDE0305 — fluent .ToList() with an explicit collection target; the law wants [.. spread].</summary>
		internal static IList<int> FluentMaterialization()
		{
			IList<int> values = Enumerable.Range(1, 10).Where(v => v > 5).ToList();
			return values;
		}

		/// <summary>CA1727 + CA2254 + CA1848 — the logging law, all three fronts.</summary>
		internal static void UnlawfulLogging(ILogger logger, int value)
		{
			logger.LogInformation($"interpolated {value}");
			logger.LogInformation("lowercase {placeholder}", value);
		}

		/// <summary>IDE0055 — the space-indented method (formatting law). Indentation below is spaces, deliberately.</summary>
		internal static int SpaceIndented()
		{
        return 7;
		}

		static int CountThings() => 42;
	}

	/// <summary>IDE0040 + IDE1006 — redundant 'private' and an m_-prefixed field.</summary>
	internal sealed class ModifierCanary
	{
		private int m_badName = 7;

		internal int Read() => m_badName;
	}

	/// <summary>CA1852 — internal type left unsealed with no derivations.</summary>
	internal class UnsealedCanary
	{
		internal static string Kind => "unsealed";
	}
}
```

The file itself is the IDE0161 canary (block-scoped namespace) and the IDE0005 canary (`using System.Text;` is never used). `m_badName` doubles for IDE0040 (redundant `private`) and IDE1006 (naming law). Expect co-travelers in the output (IDE0017 on construction shapes, CA1812 on `UnsealedCanary`) — extra errors are fine; the harness asserts the expected IDs *appear*, and the build fails either way.

- [ ] **Step 3: Run to verify green**

Run: `pwsh poc/build/Verify-Enforcement.ps1`
Expected: exit 0 — every Phase 2 ID fires as `error`; clean builds still pass (the file is out of the compilation when canaries are off).

**Contingency (record in FINDINGS either way):** if some ID doesn't fire because another error suppresses its analysis pass (the CS1591 lesson), split the non-firing canary to its own toggle following the existing `EnableDocCanaries` pattern — a third `DefineConstants`/`Compile Remove` switch and a third `Assert-CanaryBuild` call — rather than weakening assertions.

- [ ] **Step 4: Checkpoint — stage and show diff**

```
git add poc/build/src/Glitnir.Probe/StyleCanaries.cs poc/build/src/Glitnir.Probe/Glitnir.Probe.csproj poc/build/Verify-Enforcement.ps1
```
Suggested commit: `Add Phase 2 style canaries — every law cluster fires as error`

---

### Task 5: The Inverse Canary — Silent Must Stay Silent

**Files:**
- Create: `poc/build/src/Glitnir.Probe/JudgmentCitizen.cs`

- [ ] **Step 1: Write the always-compiled judgment-tier violation**

NOT canary-gated — this file is in every build. It violates IDE0046 (convert-to-conditional), which is declared `silent`; if the tier assignment ever regresses to error, the *clean* build breaks, which is exactly the alarm we want.

```csharp
namespace Glitnir.Probe;

/// <summary>Inverse canary: IDE0046 bait that must always compile clean — proves the silent tier stays silent.</summary>
public static class JudgmentCitizen
{
	/// <summary>An if/else return IDE0046 would collapse to a conditional; the judgment tier leaves it alone.</summary>
	public static string Describe(bool formal)
	{
		if (formal)
		{
			return "Citizen";
		}

		return "baw";
	}
}
```

- [ ] **Step 2: Run to verify the clean build stays clean**

Run: `pwsh poc/build/Verify-Enforcement.ps1`
Expected: exit 0 — `src/` clean build passes with the bait in the compilation.

- [ ] **Step 3: Checkpoint — stage and show diff**

```
git add poc/build/src/Glitnir.Probe/JudgmentCitizen.cs
```
Suggested commit: `Add inverse canary proving the silent tier stays silent`

---

### Task 6: Harness Hardening — the `'!~*'` Not-Contains Case (Boy Scout)

**Files:**
- Modify: `poc/build/Verify-Enforcement.ps1:43-47`

- [ ] **Step 1: Add the branch (order matters — `'!~*'` before `'!*'`)**

The `switch -Wildcard` in `Assert-Properties` becomes:

```powershell
			$ok = switch -Wildcard ($want) {
				'!~*' { $actual -notlike "*$($want.Substring(2))*" }
				'~*' { $actual -like "*$($want.Substring(1))*" }
				'!*' { $actual -ne $want.Substring(1) }
				default { $actual -eq $want }
			}
```

This upgrades the smoke floor's existing `NoWarn = '!~CS1591'` assertion from must-differ (weak) to must-not-contain (what FINDINGS recommendation #56 called for at promotion time — we're touching the harness, so it lands now).

- [ ] **Step 2: Run to verify everything still passes**

Run: `pwsh poc/build/Verify-Enforcement.ps1`
Expected: exit 0. The smoke `NoWarn` check now routes through the new branch (smoke's `NoWarn` contains `IL2121`, not `CS1591` — still green, now for the right reason).

- [ ] **Step 3: Checkpoint — stage and show diff**

```
git add poc/build/Verify-Enforcement.ps1
```
Suggested commit: `Harden harness with not-contains assertion case`

---

### Task 7: The Razor Probe

**Files:**
- Create: `poc/build/src/Glitnir.Probe.Components/Glitnir.Probe.Components.csproj`
- Create: `poc/build/src/Glitnir.Probe.Components/TabProbe.razor`
- Modify: `poc/build/Verify-Enforcement.ps1`

- [ ] **Step 1: Create the Razor class library**

`Glitnir.Probe.Components.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk.Razor">

	<ItemGroup>
		<PackageReference Include="Microsoft.AspNetCore.Components.Web" Version="10.0.0" />
	</ItemGroup>

</Project>
```

`TabProbe.razor` (tab-indented throughout — this file IS the Ctrl+K,D acceptance artifact):

```razor
<p class="probe">@_message</p>

@code {
	string _message = "tabs survive";
}
```

- [ ] **Step 2: Add harness coverage — the law must land and tolerate a Razor project**

After the existing `src/` block in `Verify-Enforcement.ps1`:

```powershell
Write-Host "`n=== src/ - razor probe (law lands; lawful razor builds clean) ==="
Assert-Properties 'src/Glitnir.Probe.Components/Glitnir.Probe.Components.csproj' $law
Assert-CleanBuild 'src/Glitnir.Probe.Components/Glitnir.Probe.Components.csproj'
```

- [ ] **Step 3: Run and adjudicate the likely CS1591 finding**

Run: `pwsh poc/build/Verify-Enforcement.ps1`
Expected: either exit 0, or the Razor probe's clean build fails with CS1591 on the generated public `TabProbe` component class. **If it fails, that's a real platform finding, not a nuisance** — every future `{Company}.{Context}.Components` project lives in `src/`-tier law, so "are Blazor components documented public surface?" needs a ruling. Record it in the FINDINGS Phase 2 appendix and surface it to Buvy at the next checkpoint — do NOT silently NoWarn it. (Interim unblock if he rules components exempt: `<NoWarn>$(NoWarn);CS1591</NoWarn>` in this csproj with a comment citing the ruling.)

- [ ] **Step 4: Checkpoint — stage and show diff**

```
git add poc/build/src/Glitnir.Probe.Components/ poc/build/Verify-Enforcement.ps1
```
Suggested commit: `Add Razor probe for the law and the Ctrl+K,D acceptance check`

---

### Task 8: The `.slnx` Test-Drive Solution

**Files:**
- Create: `poc/build/Glitnir.BuildLaw.slnx`

- [ ] **Step 1: Create the solution (slnx is mandatory — never legacy .sln)**

Run from `poc/build/`:

```
dotnet new sln --format slnx --name Glitnir.BuildLaw
dotnet sln Glitnir.BuildLaw.slnx add src/Glitnir.Probe/Glitnir.Probe.csproj src/Glitnir.Probe.Components/Glitnir.Probe.Components.csproj tests/Glitnir.Probe.Tests/Glitnir.Probe.Tests.csproj benchmarks/Glitnir.Probe.Benchmarks/Glitnir.Probe.Benchmarks.csproj poc/Glitnir.Probe.Severed/Glitnir.Probe.Severed.csproj tests/smoke/Glitnir.Probe.Smoke/Glitnir.Probe.Smoke.csproj
```

All six projects deliberately — the VS test drive should show governed *and* severed behavior side by side.

- [ ] **Step 2: Verify the solution builds**

Run from `poc/build/`: `dotnet build Glitnir.BuildLaw.slnx -tl:off --nologo`
Expected: success — every project clean under the full law (canaries off).

- [ ] **Step 3: Checkpoint — stage and show diff**

```
git add poc/build/Glitnir.BuildLaw.slnx
```
Suggested commit: `Add slnx solution for the VS test drive`

---

### Task 9: Test-Drive Checklist + FINDINGS Appendix

**Files:**
- Modify: `poc/build/README.md` (append section)
- Modify: `poc/build/FINDINGS.md` (append Phase 2 appendix)

- [ ] **Step 1: Append the test-drive checklist to `README.md`**

```markdown
## VS Test Drive (Phase 2 acceptance — muscle memory edition)

Open `Glitnir.BuildLaw.slnx` in Visual Studio (SDK resolves from the repo root `global.json`). Record the VS version — it becomes the pinned workstation floor.

1. **Razor tabs (the old wound):** open `TabProbe.razor` → Ctrl+A, Ctrl+K,D → indentation stays TABS (View → check whitespace rendering). Then add a spaces-indented line inside `@code` → Ctrl+K,D → it converts to tabs. If either fails: Tools → Options → Text Editor → Razor — check for a legacy-formatter toggle, and record the finding.
2. **C# format is a no-op on lawful code:** open `LawfulCitizen.cs` → Ctrl+K,D → zero changes.
3. **Live law:** in `LawfulCitizen.cs`, type `private` in front of a bare member → IDE0040 error squiggle (red, not suggestion). Undo.
4. **The slop ratchet:** add `using System.Text;` at the top → grayed immediately; build → IDE0005 *error*. Undo.
5. **The var law, live:** type `var c = new LawfulCitizen();` → IDE0008 error; rewrite `LawfulCitizen c = new LawfulCitizen();` → IDE0090 error; `LawfulCitizen c = new();` → clean. Undo.
6. **Naming law:** add field `int m_test;` → IDE1006 error squiggle. Undo.
7. **Judgment tier stays quiet:** open `JudgmentCitizen.cs` → the if/else return shows at most a faint suggestion, no squiggle, and never appears in Error List as error/warning.
8. **Severed contrast:** open `poc/Glitnir.Probe.Severed/Outlaw.cs` — IDE may show law squiggles (editorconfig reaches the editor) but the project still BUILDS clean: severance lives in the props, not the editorconfig. Expected, by design.
9. **The gate:** `pwsh ./Verify-Enforcement.ps1` → exit 0.
```

- [ ] **Step 2: Append a Phase 2 appendix skeleton to `FINDINGS.md`**

```markdown
---

# Phase 2 Appendix — .editorconfig Curation (executed 2026-06-06)

**SDK:** (record resolved version)  **Harness:** (record exit + run count)

## Verdicts

| Claim | Verdict |
|---|---|
| Full style law tolerates lawful probe code unchanged | (record) |
| Every Phase 2 canary ID fires as error in one build | (record) |
| Silent tier stays silent (inverse canary compiles clean) | (record) |
| CA1848/CA2254/CA1852 reachable via category knobs alone (no editorconfig lines) | (record) |
| CA1727 requires the targeted editorconfig severity | (record) |
| Razor project builds clean under full src/ law | (record — CS1591-on-components ruling if hit) |
| JSON source-gen switch lands in src/ and only src/ | (record) |

## Deviations and Surprises

(numbered, same format as Phase 1 — every co-firing diagnostic, suppressed pass, or option-name correction goes here)

## VS Test Drive Results

(filled by Buvy's session — VS version pinned: _____)
```

These tables are *skeletons by design* — the executing engineer fills verdicts from actual harness output during this task, and the test-drive block is Buvy's to fill from VS.

- [ ] **Step 3: Checkpoint — stage and show diff**

```
git add poc/build/README.md poc/build/FINDINGS.md
```
Suggested commit: `Add VS test-drive checklist and Phase 2 findings appendix`

---

### Task 10: Doc Amendments (Spec §10 Ledger)

**Files:**
- Modify: `CLAUDE.md` (§4 Runtime and Language; §8 Type Safety)
- Modify: `docs/conventions.md`
- Modify: `docs/spec-reconciliation-2026-06-04.md` (item 4.2)
- Modify: `docs/superpowers/specs/2026-06-05-build-enforcement-design.md` (§5)

- [ ] **Step 1: CLAUDE.md §4 — tab width + the codified law**

Find: `- **Tabs, 2-space width** (Markdown and YAML excepted).`
Replace with:

```markdown
- **Tabs, 4-space width.** Whitespace-aware/ecosystem exceptions declared in the root `.editorconfig` with reasons: YAML/Markdown/JSON 2-space, Python/F# 4-space.
- **Don't fight the ecosystem.** Where an ecosystem's standard tooling has a fixed convention (Black's 4-space Python, dotnet CLI's 2-space JSON), the ecosystem wins and the exception is declared inline with its reason.
```

- [ ] **Step 2: CLAUDE.md §8 — the null-collection anti-pattern**

In §8 → "Type Safety and Domain Modeling", after the `No implicit enum values` bullet, add:

```markdown
- **No null collections.** Absence of items is `[]`, never null; `?` on an enumerable-shaped type is a design error. Null is reserved for genuinely optional references (NRT-declared) and `Nullable<T>` structs. Compiler-enforced for non-nullable returns (NRT + ratchet); declaration ban is YGG analyzer bench until the rule lands.
```

- [ ] **Step 3: conventions.md — async laws + style pointer**

Append to `docs/conventions.md`:

```markdown
## Async

- **The elide law.** A method that does no work after its last await neither marks `async` nor awaits — it returns the `Task` directly. `await` exists only when there is work after the resumption. **Exception, load-bearing:** the await stays when the task is produced inside a `try`/`catch`/`finally` or `using` scope — eliding there lets the task escape the scope (the connection disposes before the query completes; exceptions detach from their handlers). Elide only pure tail positions. Enforcement: YGG analyzer bench (editorconfig spec §9); review-enforced until then.
- **Concurrent awaits use the tuple idiom.** Independent async operations are awaited concurrently as a tuple — `var (quote, risk) = await (GetQuoteAsync(id), GetRiskAsync(id));` (TaskTupleAwaiter) — not sequentially, and not via `Task.WhenAll` ceremony for disparate types.
- **ConfigureAwait discipline is already build law** — CA2007 is an error platform-wide (Reliability `latest-All`).

## Style Law

The complete style, formatting, and naming law lives in the root `.editorconfig`, designed in `docs/superpowers/specs/2026-06-05-editorconfig-curation-design.md`. Headline rules: tabs (width 4); var everywhere except construction (type left, `new()` right); file-scoped namespaces; `omit_if_default` accessibility; `_camelCase` private fields; collection expressions (`[]` is the only legal empty); IDE0005 unnecessary-using as build error.
```

- [ ] **Step 4: Reconciliation punch list — close 4.2 Phase 2 mechanics**

In `docs/spec-reconciliation-2026-06-04.md` item 4.2, append after the absorbed-mechanics bullet list:

```markdown
**Phase 2 executed (2026-06-06):** `.editorconfig` curated per `docs/superpowers/specs/2026-06-05-editorconfig-curation-design.md` — `omit_if_default` ✓, CA1848/CA2254 ✓ (category-knob reach, canary-proven), CA1727 ✓ (targeted severity), CA1852 ✓ (canary-proven), `JsonSerializerIsReflectionEnabledByDefault` ✓ (relocated to `src/`-only delta by ruling), root config files ✓ (committed 2026-06-05). Remaining from 4.2: real-tree seeding, `UseProjectReferences` session.
```

- [ ] **Step 5: Build-enforcement spec §5 — the `src/` delta**

In `docs/superpowers/specs/2026-06-05-build-enforcement-design.md` §5, the `src/Directory.Build.props` snippet becomes:

```xml
	<PropertyGroup>
		<JsonSerializerIsReflectionEnabledByDefault>false</JsonSerializerIsReflectionEnabledByDefault>
	</PropertyGroup>

	<ItemGroup>
		<InternalsVisibleTo Include="$(AssemblyName).Tests" />
	</ItemGroup>
```

with the sentence: "The JSON source-gen switch is a `src/`-only delta by ruling (2026-06-06): tests and benchmarks reflect-serialize freely; only working software is held to the source-gen posture."

- [ ] **Step 6: Checkpoint — stage and show diff**

```
git add CLAUDE.md docs/conventions.md docs/spec-reconciliation-2026-06-04.md docs/superpowers/specs/2026-06-05-build-enforcement-design.md
```
Suggested commit: `Apply editorconfig spec amendments — tab width 4, null collections, async laws`

---

### Task 11: Final Gate + Handoff to the Test Drive

- [ ] **Step 1: Full harness run, fresh**

Run: `pwsh poc/build/Verify-Enforcement.ps1`
Expected: exit 0, all sections green — law lands, canaries fire as errors, silent stays silent, clean builds pass.

- [ ] **Step 2: Full solution build**

Run from `poc/build/`: `dotnet build Glitnir.BuildLaw.slnx -tl:off --nologo`
Expected: success.

- [ ] **Step 3: Update FINDINGS verdicts**

Fill the Phase 2 appendix verdict table from actual output (Task 9 skeleton). Leave only the VS test-drive block open.

- [ ] **Step 4: Present the complete staged diff to Buvy**

```
git status --short
git diff --cached --stat
```

Hand off: the VS test drive (README checklist) is his; the stray `.editorconfig` one level above the workspace root (spec §11 housekeeping) is a machine-local deletion to do alongside it — after confirming nothing else in that directory wants it.

---

## Self-Review Notes

- **Spec coverage:** §3 anatomy → Task 1; §4 non-C# + razor → Tasks 1, 7; §5/§6/§7 law → Task 1; §5 IDE0005/IDE0305 canaries + §11 verification table → Tasks 3–5; ruling #12 → Task 2; §11 harness/inverse/landing → Tasks 3–6; §11 razor check + housekeeping → Tasks 7, 9, 11; §10 amendments → Task 10. §8/§9 (contours + bench) need no code — they land via Task 10's doc edits. IDE0306 added beyond the spec's 0300–0305 range: same family, new in current SDKs; record in FINDINGS if it's unknown to the preview SDK (assertion would fail loudly, not silently).
- **Known empirical risks, each with a recorded contingency:** style-canary co-fire suppression (Task 4), CS1591 on Razor components (Task 7 — surfaced as a ruling, never silently suppressed), option-name drift on the preview SDK (any unknown option is inert by editorconfig semantics — the canaries are what catch a law that didn't land).
- **Naming-rule order** is load-bearing (constants before private fields) and stated in the file itself.
