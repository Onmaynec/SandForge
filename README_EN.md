# SandForge

> Forge disposable Windows environments.

The default project documentation is maintained in Russian: [README.md](README.md).

SandForge `0.3.0-alpha` is a .NET 8 CLI manager for Windows Sandbox sessions. It provides safe templates, SHA-256 verification, SQLite history, installer before/after collectors, crash recovery, cleanup commands and offline HTML/JSON reports.

```powershell
sandforge doctor
sandforge run .\Application.exe
sandforge test-installer .\Setup.exe
sandforge recover
sandforge cleanup --dry-run --older-than 30d
```

See the Russian documentation for the complete command and security reference.
