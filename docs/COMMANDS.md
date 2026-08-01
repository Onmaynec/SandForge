# ⌨️ Команды SandForge 0.2

## Общие команды

```powershell
sandforge --help
sandforge --version
sandforge doctor
```

## Запуск файлов

```powershell
sandforge run .\Application.exe
sandforge run .\Application.exe --template .\templates\isolated-analysis\sandforge.yaml
sandforge run-script .\script.ps1
sandforge test-installer .\Setup.exe
```

`test-installer` использует встроенный шаблон `installer-test` и активирует before/after collectors.

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

## Exit codes

| Код | Значение |
|---:|---|
| `0` | успех |
| `1` | общая ошибка |
| `2` | неверные аргументы |
| `3` | файл или шаблон не найден |
| `4` | ошибка валидации |
| `5` | Windows Sandbox недоступна |
| `7` | запуск заблокирован политикой безопасности |
| `9` | частичный результат |
| `10` | сессия завершилась ошибкой |
| `11` | timeout |
| `12` | отменено пользователем |

## Matrix Runner (`0.3.0-alpha`)

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

`update check` возвращает exit code `20`, когда новая версия найдена. Это позволяет использовать проверку в скриптах.
