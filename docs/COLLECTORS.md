# 🔎 Коллекторы SandForge

Коллекторы версии `0.2.0-alpha` выполняются внутри Windows Sandbox.

| ID | Результат |
|---|---|
| `process-list` | список процессов после запуска цели |
| `installed-apps` | добавленные, удалённые и изменённые записи приложений |
| `file-changes` | изменения файлов в выбранных Program Files, ProgramData и LocalAppData |
| `registry-changes` | изменения выбранных Run и Uninstall-разделов |
| `services` | изменения служб Windows |
| `scheduled-tasks` | изменения запланированных задач |
| `user-output` | пользовательские файлы из output-папки |

Diff-файлы находятся в:

```text
output\.sandforge\collectors
```

После импорта они копируются в `artifacts\collectors` и получают SHA-256.

## Ограничения

- file collector ограничен 50 000 записями;
- registry collector не является полным снимком реестра;
- некоторые свойства недоступны без прав или в зависимости от редакции Windows;
- collector error сохраняется отдельно и не скрывает результаты остальных collectors.

## Формат результата

Каждый collector создаёт самостоятельный JSON-файл:

```json
{
  "collector": "services",
  "items": [],
  "error": null
}
```

При ошибке поле `error` содержит диагностическое сообщение, `items` остаётся валидным массивом, а остальные collectors продолжают работу.
