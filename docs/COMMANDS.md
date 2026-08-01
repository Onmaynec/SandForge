# ⌨️ Команды SandForge

## Общие

```powershell
sandforge --help
sandforge --version
sandforge doctor
```

## Запуск

```powershell
sandforge run <file>
sandforge run <file> --template .\templates\isolated-analysis\sandforge.yaml
sandforge run-script <file.ps1>
```

## Сессии

```powershell
sandforge session list
sandforge session show <session-id>
```

## Отчёты

```powershell
sandforge report <session-id>
sandforge report <session-id> --format json
sandforge report <session-id> --format html
```

## Exit codes

| Код | Значение |
|---:|---|
| 0 | успех |
| 1 | общая ошибка |
| 2 | неверные аргументы |
| 3 | файл или шаблон не найден |
| 4 | шаблон не прошёл проверку |
| 5 | Windows Sandbox недоступна |
| 7 | security policy заблокировала запуск |
| 9 | сессия завершена частично |
| 10 | сессия завершилась ошибкой |
| 11 | timeout |
| 12 | отменено пользователем |
