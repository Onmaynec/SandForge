# 🩺 Устранение неполадок

## Windows Sandbox недоступна

```powershell
sandforge doctor
```

Проверь Windows 10/11 x64, поддерживаемую редакцию, аппаратную виртуализацию и компонент `Containers-DisposableClientVM`.

## Сессия осталась Running

После аварийного завершения host-процесса:

```powershell
sandforge recover
```

Если marker отсутствует, сессия станет `Orphaned`. Workspace сохраняется для ручного анализа.

## База данных не создаётся

Проверь права записи в `%LocalAppData%\SandForge` или включи portable mode. Файл базы: `sandforge.db`.

## Collector показывает ошибку

Проверь файлы:

```text
output\.sandforge\collector-before-error.txt
output\.sandforge\collector-after-error.txt
output\.sandforge\bootstrap-error.txt
```

Отдельный collector может быть недоступен в конкретной сборке Windows Sandbox, не отменяя остальные результаты.

## Cleanup ничего не удаляет

Сначала используй:

```powershell
sandforge cleanup --dry-run --older-than 1h
```

Сессии `Kept`, активные и слишком новые не являются кандидатами.
