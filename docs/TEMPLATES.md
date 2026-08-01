# 🧩 Шаблоны SandForge

SandForge поддерживает ограниченный безопасный поднабор YAML без произвольных tags, конструкторов объектов и динамического кода.

```yaml
schemaVersion: 1
metadata:
  name: installer-test
  displayName: Проверка установщика
  description: Снимки до и после запуска
sandbox:
  network: disabled
  clipboard: disabled
  protectedClient: true
  memoryMb: 4096
session:
  timeout: 30m
  keepWorkspace: false
target:
  executable: "C:\Sandbox\Input\${targetFileName}"
  workingDirectory: "C:\Sandbox\Input"
  arguments:
artifacts:
  collectors:
    - process-list
    - installed-apps
    - file-changes
    - registry-changes
    - services
    - scheduled-tasks
    - user-output
```

## Встроенные шаблоны

| Шаблон | Назначение |
|---|---|
| `isolated-analysis` | безопасный запуск неизвестного файла без сети |
| `powershell-clean` | выполнение PowerShell-скрипта |
| `minimal` | минимальная сессия |
| `installer-test` | before/after анализ установщика |

Неизвестные верхнеуровневые секции не исполняются. `schemaVersion`, memory и timeout валидируются до запуска.
