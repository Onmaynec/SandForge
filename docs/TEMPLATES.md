# 🧩 Шаблоны SandForge 0.5.0

SandForge поддерживает ограниченный безопасный поднабор YAML без произвольных tags, конструкторов объектов и динамического кода.

Текущая версия контракта шаблона — `schemaVersion: 2`. Версия `1` остаётся поддерживаемой как deprecated и выдаёт предупреждение.

```yaml
schemaVersion: 2
extends: "../common/base.yaml"
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

## Проверка без запуска

```powershell
sandforge schema describe template
sandforge schema validate .\templates\minimal\sandforge.yaml
```

Неизвестная или неподдерживаемая версия схемы отклоняется до создания workspace с exit code `4`.

## Встроенные шаблоны

| Шаблон | Назначение |
|---|---|
| `isolated-analysis` | безопасный запуск неизвестного файла без сети |
| `powershell-clean` | выполнение PowerShell-скрипта |
| `minimal` | минимальная сессия |
| `installer-test` | before/after анализ установщика |

Неизвестные верхнеуровневые секции не исполняются. `schemaVersion`, memory, timeout, mounts и collectors валидируются до запуска.

## SchemaVersion 2: extends и includes

```yaml
schemaVersion: 2
extends: "../common/base.yaml"
includes:
  - "../common/collectors.yaml"
```

Сначала применяется `extends`, затем `includes` в указанном порядке, затем текущий файл. Scalars из текущего файла переопределяют base. Mounts объединяются по `destination`, packages — по `id`, installers — по source path, collectors и cache types дедуплицируются.

Все ссылки должны оставаться внутри корня `templates`. Циклические ссылки и path traversal блокируются до создания workspace.

## Legacy schemaVersion 1

Шаблоны schema `1` можно читать и запускать в `0.5.0`, но SandForge выдаёт предупреждение об устаревании. Новые шаблоны должны использовать schema `2`.

Перед миграцией legacy-шаблона:

```powershell
sandforge schema validate .\legacy-template.yaml --contract template
```

После перехода на schema `2` можно использовать `extends/includes`, не дублируя общий security baseline.

## Provisioning

```yaml
provisioning:
  failurePolicy: stop
  packages:
    - id: Git.Git
      version: "2.50.0"
      source: winget
  installers:
    - path: "./payload/setup.msi"
      sha256: "<64 hex>"
      timeout: 10m
      arguments:
        - "PROPERTY=value"
```

## Managed cache

```yaml
cache:
  enabled: true
  maximumSizeMb: 2048
  types:
    - nuget
    - npm
```

Полный compatibility contract и правила breaking changes: [COMPATIBILITY.md](COMPATIBILITY.md).
