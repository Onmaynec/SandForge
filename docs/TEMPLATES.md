# 🧩 Шаблоны

MVP поддерживает ограниченный безопасный поднабор YAML. Произвольные YAML tags, конструкторы объектов и динамические выражения не выполняются.

```yaml
schemaVersion: 1
metadata:
  name: isolated-analysis
  displayName: Isolated Analysis
  description: Restricted sandbox
sandbox:
  network: disabled
  clipboard: disabled
  protectedClient: true
  memoryMb: 4096
session:
  timeout: 15m
  keepWorkspace: false
target:
  executable: "C:\\Sandbox\\Input\\${targetFileName}"
  workingDirectory: "C:\\Sandbox\\Input"
  arguments:
    - "--example"
artifacts:
  collectors:
    - user-output
```

## Поддерживаемые поля

- `schemaVersion` — только `1`;
- `metadata` — `name`, `displayName`, `description`;
- `sandbox` — `network`, `clipboard`, `protectedClient`, `memoryMb`;
- `session` — `timeout`, `keepWorkspace`;
- `mounts` — `source`, `destination`, `mode`;
- `target` — `executable`, `workingDirectory`, `arguments`, `wait`;
- `artifacts.collectors` — сейчас фактически реализован `user-output`.

## Mount modes

| Mode | Поведение |
|---|---|
| `readOnly` | host directory монтируется без записи |
| `readWrite` | guest получает запись; повышенный риск |
| `copyIn` | зарезервировано для следующей итерации |
| `copyOut` | зарезервировано для следующей итерации |

> [!CAUTION]
> Writable mount системного диска или чувствительной директории блокируется.
