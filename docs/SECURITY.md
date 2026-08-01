# 🔐 Модель безопасности

## Значения по умолчанию

- сеть выключена;
- clipboard выключен;
- input копируется в session workspace;
- output создаётся отдельно;
- protected client включён;
- timeout обязателен;
- артефакты не открываются автоматически.

## Риски

- **Low** — offline, clipboard disabled, isolated output;
- **Medium** — сеть или clipboard включены;
- **High** — writable host mount или чрезмерный timeout;
- **Critical** — writable system drive, профиль пользователя или credential locations; запуск блокируется.

## Защита данных

- SHA-256 для цели и импортированных артефактов;
- проверка выхода путей за пределы workspace;
- квоты количества и размера артефактов;
- HTML escaping;
- проверка marker по `schemaVersion` и `sessionId`;
- collectors работают в guest и не сканируют host.

> SandForge не является антивирусом, EDR или гарантией абсолютной изоляции.
