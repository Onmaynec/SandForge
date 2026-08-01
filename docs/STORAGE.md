# 🗄️ SQLite-хранилище

Файл базы данных:

```text
%LocalAppData%\SandForge\sandforge.db
```

В portable mode:

```text
data\sandforge.db
```

## Миграции

Таблица `MigrationHistory` фиксирует применённые версии схемы. В `0.2.0-alpha` используется migration version `1`.

При первом запуске SandForge ищет прежний файл:

```text
sessions\index.json
```

Найденные записи импортируются в SQLite, после чего файл переименовывается в `index.json.migrated`.

## Транзакционность

Обновление `Sessions`, `SessionArtifacts` и `CollectorResults` выполняется одной транзакцией, чтобы сессия не оставалась с частично записанным списком артефактов.
