# ⌨️ Команды SandForge 0.5.0

## Общие команды

```powershell
sandforge
sandforge --help
sandforge --version
sandforge doctor
```

Запуск без аргументов открывает интерактивный TUI.

## Запуск файлов

```powershell
sandforge run .\Application.exe
sandforge run .\Application.exe --template .\templates\isolated-analysis\sandforge.yaml
sandforge run-script .\script.ps1
sandforge test-installer .\Setup.exe
```

`test-installer` использует встроенный шаблон `installer-test` и активирует before/after collectors.

## Проверка схем и совместимости

```powershell
sandforge schema list
sandforge schema describe template
sandforge schema describe report
sandforge schema validate .\templates\minimal\sandforge.yaml
sandforge schema validate .\report.json --contract report
```

Команды:

- `schema list` — показать зарегистрированные контракты, текущие, поддерживаемые и устаревшие версии;
- `schema describe <id>` — показать сведения о конкретном контракте;
- `schema validate <file>` — автоматически определить контракт и проверить документ;
- `--contract <id>` — явно выбрать контракт, когда автоматическое определение неоднозначно.

Exit code `4` означает невалидный документ, неизвестный контракт или неподдерживаемую версию схемы. Подробности: [COMPATIBILITY.md](COMPATIBILITY.md).

## История сессий

```powershell
sandforge session list
sandforge session show <session-id>
sandforge session delete <session-id>
```

История хранится в SQLite: `%LocalAppData%\SandForge\sandforge.db` или `data\sandforge.db` в portable mode.

## Отчёты

```powershell
sandforge report <session-id>
sandforge report <session-id> --format json
sandforge report <session-id> --format html
```

JSON-отчёты версии `0.5.0` используют report schema `1` и содержат `schemaVersion`, `language`, `generatedAt`, `generatorVersion` и объект сессии. Legacy-отчёты без `schemaVersion` распознаются как schema `0` и открываются с предупреждением.

## Восстановление

```powershell
sandforge recover
```

Проверяются сессии со статусами `Starting`, `Running` и `Collecting`.

## Очистка

```powershell
sandforge cleanup --dry-run
sandforge cleanup --older-than 30d
sandforge cleanup --orphaned --older-than 1h
```

Параметры:

- `--dry-run` — показать кандидатов без удаления;
- `--older-than 30d` — минимальный возраст (`d` или `h`);
- `--orphaned` — обрабатывать только orphaned-сессии.

## Matrix Runner

```powershell
sandforge matrix run .\Application.exe --templates minimal,isolated-analysis
sandforge matrix run .\Application.exe --templates minimal,installer-test --parallel 2
```

Каждый шаблон создаёт отдельную сессию. Допустимый `--parallel`: от 1 до 4.

## Managed cache

```powershell
sandforge cache list
sandforge cache clean nuget --dry-run
sandforge cache clean nuget
sandforge cache clean
```

## Обновления GitHub Releases

```powershell
sandforge update status
sandforge update check
sandforge update install [--yes]
sandforge update auto on|off [--apply]
sandforge update channel stable|preview
```

`update check` возвращает exit code `20`, когда найдена новая версия. Релиз `v0.5.0` публикует Windows x64 ZIP и отдельный SHA-256 checksum.

## Exit codes

| Код | Значение |
|---:|---|
| `0` | успех |
| `1` | общая ошибка |
| `2` | неверные аргументы или использование команды |
| `3` | файл или шаблон не найден |
| `4` | ошибка валидации, неизвестная или неподдерживаемая схема |
| `5` | Windows Sandbox недоступна |
| `7` | запуск заблокирован политикой безопасности |
| `9` | частичный результат |
| `10` | сессия завершилась ошибкой |
| `11` | timeout |
| `12` | отменено пользователем |
| `20` | доступно обновление |
