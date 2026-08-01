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

## Обновления 0.3

Updater принимает package только по HTTPS с GitHub, требует отдельный SHA-256 asset, проверяет ZIP path traversal, создаёт backup и выполняет rollback при неуспешном self-check. Автоустановка отключена по умолчанию.

## Provisioning 0.3

Package provisioning разрешён только с включённой сетью guest. Поддерживается только `winget`. Локальные installers копируются в workspace и проверяются SHA-256 на host и в guest. Provisioning не получает host credentials или пользовательские package caches.

## Managed cache 0.3

Cache хранится в отдельном каталоге SandForge и не является прямым mount пользовательских NuGet/npm/pip каталогов. Поддерживается allowlist типов и quota. Включение writable managed cache повышает risk до Medium.
