# Compatibility policy

SandForge 0.5 introduces explicit versions for every file format that crosses a process, package or release boundary.

## Registered contracts

| Contract | Current | Supported | Deprecated | Schema |
|---|---:|---|---|---|
| `template` | 2 | 1, 2 | 1 | `schemas/template.schema.json` |
| `config` | 2 | 2 | — | `schemas/config.schema.json` |
| `report` | 1 | 0, 1 | 0 | `schemas/report.schema.json` |
| `completion-marker` | 1 | 1 | — | `schemas/completion-marker.schema.json` |
| `package-manifest` | 1 | 1 | — | `schemas/package-manifest.schema.json` |

The machine-readable registry is stored in `schemas/catalog.json`.

## CLI

```text
sandforge schema list
sandforge schema describe template
sandforge schema validate templates/base/sandforge.yaml
sandforge schema validate report.json --contract report
```

Validation returns exit code `0` for a compatible document, `2` for invalid command usage and `4` for an invalid, unknown or unsupported contract.

## Version rules

- `schemaVersion` is required for all current JSON contracts.
- A supported deprecated version can be read, but produces a warning.
- An unsupported version is rejected before execution or import.
- Adding optional fields is backward compatible.
- Removing or renaming a field, changing its meaning or changing an enum value requires a new schema version.
- Domain/error codes are language-neutral and are not translated.
- JSON property names and enum values are stable lower camel case.

## Report migration

Reports generated before 0.5 did not contain `schemaVersion`, `generatedAt` or `generatorVersion`. They are detected as report schema `0`, remain readable during the alpha period and produce a deprecation warning. New reports use schema `1`.

## Portable package manifest

`package.ps1` creates `manifest.json` before the ZIP archive is produced. The manifest records the product version, runtime identifier, creation time and SHA-256 of every packaged file. Paths must be relative and may not contain `..` segments.

The manifest proves package integrity only when it is obtained through a trusted channel. Cryptographic signing and trust-chain validation remain a separate 1.0 milestone.

## Toward 1.0

The contracts introduced in 0.5 are still alpha contracts. Before 1.0 the project will add migration fixtures, long-term compatibility windows and signed release metadata. After 1.0, breaking contract changes require a major release.
