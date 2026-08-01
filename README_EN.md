# SandForge

> Forge disposable Windows environments.

The primary project documentation is maintained in Russian: [README.md](README.md).

SandForge `0.4.0-alpha` is a .NET 8 manager for reproducible Windows Sandbox sessions. It includes a keyboard-first Spectre.Console TUI, security-plan preview, safe templates, SHA-256 verification, SQLite history, installer before/after collectors, recovery, managed cache, GitHub Release updates and offline reports.

```powershell
sandforge
```

Running without arguments opens the interactive dashboard. Existing CLI commands remain available:

```powershell
sandforge doctor
sandforge run .\Application.exe
sandforge test-installer .\Setup.exe
sandforge matrix run .\Application.exe --templates minimal,isolated-analysis
sandforge session list
sandforge report <session-id> --format html
sandforge recover
sandforge cleanup --dry-run --older-than 30d
```

Set the UI language in `sandforge.json`:

```json
{
  "ui": {
    "language": "en"
  }
}
```

Supported values are `ru`, `en` and `auto`. You can also set `SANDFORGE_LANGUAGE=en` for a temporary override.

See [docs/TUI.md](docs/TUI.md), [docs/LOCALIZATION.md](docs/LOCALIZATION.md) and the Russian README for the complete command and security reference.
