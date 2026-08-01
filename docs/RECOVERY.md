# ♻️ Восстановление и очистка

## Crash recovery

```powershell
sandforge recover
```

Алгоритм:

1. загрузить активные записи из SQLite;
2. найти workspace каждой сессии;
3. проверить сохранённый PID и `output\.sandforge\completed.json`;
4. проверить `schemaVersion` и `sessionId`;
5. импортировать артефакты и collector results;
6. оставить живую сессию активной либо отметить завершённую запись как `Completed`, `Partial` или `Orphaned`.

## Cleanup

```powershell
sandforge cleanup --dry-run --older-than 30d
sandforge cleanup --older-than 30d
```

Перед удалением SandForge нормализует путь и проверяет, что workspace расположен внутри собственного каталога `sessions`. Пути из изменённой или повреждённой базы за пределами этого каталога игнорируются. Сессии с `CleanupState=Kept` не удаляются. После удаления история остаётся в SQLite со статусом `Cleaned`.
