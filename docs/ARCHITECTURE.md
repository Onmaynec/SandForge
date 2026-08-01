# 🧱 Архитектура SandForge

## Поток версии 0.2

```text
YAML template
  → SessionPlan + SHA-256
  → SecurityPolicyEngine
  → SessionWorkspace
  → .wsb + bootstrap.ps1
  → before snapshot
  → target execution
  → after snapshot + diff
  → completion marker
  → ArtifactManager
  → SQLite SessionStore
  → console / JSON / HTML report
```

## Проекты

| Проект | Ответственность |
|---|---|
| `SandForge.Domain` | модели сессий, рисков, collectors и cleanup |
| `SandForge.Core` | шаблоны, планирование, SQLite, recovery, cleanup, импорт артефактов |
| `SandForge.Sandbox` | доступность backend, `.wsb`, guest bootstrap и collectors |
| `SandForge.Reporting` | русские console/JSON/HTML-отчёты |
| `SandForge.Cli` | команды и интерактивное меню |

## SQLite

`SessionStore` создаёт `sandforge.db` и таблицы:

- `MigrationHistory`;
- `Sessions`;
- `SessionArtifacts`;
- `CollectorResults`.

Запись сессии и связанных результатов выполняется транзакционно. Старая история `sessions/index.json` импортируется один раз и переименовывается в `index.json.migrated`.

## Guest collectors

Коллекторы генерируются как часть bootstrap и выполняются только внутри guest. Host не сканирует процессы, реестр или установленные приложения основной системы.

## Recovery

Host считает активными записи `Starting`, `Running` и `Collecting`. Если после перезапуска найден валидный marker, артефакты импортируются. При отсутствии marker сессия помечается `Orphaned` и не удаляется автоматически.
