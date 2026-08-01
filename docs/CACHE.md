# ⚡ Managed cache

SandForge не подключает пользовательские package caches. Вместо этого используется отдельный каталог `%LocalAppData%\SandForge\cache` или `data\cache` в portable mode.

Поддерживаемые типы: `nuget`, `npm`, `pip`, `winget`.

```yaml
schemaVersion: 2
cache:
  enabled: true
  maximumSizeMb: 2048
  types:
    - nuget
    - npm
```

Cache подключается только после явного включения шаблоном. Перед сессией SandForge удаляет самые старые файлы, если quota превышена. Guest получает отдельные environment variables для NuGet, npm и pip.

```powershell
sandforge cache list
sandforge cache clean nuget --dry-run
sandforge cache clean nuget
sandforge cache clean
```
