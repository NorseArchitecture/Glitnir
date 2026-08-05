# EF temporal-emission seam findings

**Run:** 2026-08-05, .NET SDK `11.0.100-preview.6.26359.118`, EF Core/Npgsql `11.0.0-preview.6.26359.118`, PostgreSQL `19beta2`.

## Verdict

Tasks 5–7 must use **both** seams: a derived `NpgsqlAnnotationProvider` to project `Norse:Temporal` onto relational table annotations, and target-model consultation inside `NpgsqlMigrationsSqlGenerator.Generate` for evolution operations. The provider is necessary for EF to diff annotation-only marker transitions; target-model lookup identifies the table for create and all five ordinary evolution operations, whose operations do not themselves carry the marker. On marker removal, the target model has no marked entity, but `AlterTableOperation.OldTable` carries `Norse:Temporal=True`; this is the required drop-side discriminator.

`TableBuilder<T>.HasAnnotation` is not present in this EF 11 preview. The variants use the equivalent relational mapping call `tb.Metadata.SetAnnotation("Norse:Temporal", true)`.

## Seven-shape evidence

| Shape | Target-model-only | Annotation-provider observations | Required generator decision | Verdict |
|---|---|---|---|---|
| Create | An entity-free baseline produces `Shape_1_Create` itself as a real `CreateTableOperation`; it has no marker and the target model resolves `widgets` temporal. | `Shape_1_Create` carries `Norse:Temporal=True`. | Marker annotation or target-model lookup. | Works |
| Add column | `AddColumnOperation` has no marker; target model resolves temporal. | Same operation annotation result. | Target-model lookup. | Works |
| Rename column | `RenameColumnOperation` has no marker; target model resolves temporal. | Same operation annotation result. | Target-model lookup. | Works |
| Drop column | `DropColumnOperation` has no marker; target model resolves temporal. | Same operation annotation result. | Target-model lookup. | Works |
| Alter column | `AlterColumnOperation` has no marker; target model resolves temporal. | Same operation annotation result. | Target-model lookup. | Works |
| Marker added | EF emits no operation. | EF emits `AlterTableOperation` with `Norse:Temporal=True`; target model resolves temporal. | New-table annotation (target lookup is corroborating). | Works only with provider |
| Marker removed | EF emits no operation. | EF emits `AlterTableOperation` with empty new annotations and `OldTable` `Norse:Temporal=True`; target model does not resolve temporal. | `OldTable` annotation. | Works only with provider |

The evidence is in `artifacts/<shape>/<mode>/operation-report.txt`; each mode also retains its preceding `baseline/` migration source. The create generated SQL and live `database update` ran twice per mode; `widgets_temporal_spike_apparatus` is the hand-written create-only apparatus emitted by the override. All fourteen scaffold/script/apply passes completed successfully, with Shape 1 re-run from an entity-free baseline after review.

## Consequences

The catastrophic condition did not occur: the differ does not suppress annotation-only transitions **when the relational annotation provider exposes the marker**. Without that provider, both transitions are silently absent, so a generator alone cannot implement enable/disable. A custom `IMigrationsModelDiffer` is therefore not indicated by this spike.

The spike does not prove full temporal DDL fidelity. It proves only the supported EF seams and one executable custom-create emission; Tasks 5–7 still own the apparatus and all evolution DDL. `DropTableOperation` is instrumented by the probe but is not a verified shape in this seven-shape gate.
