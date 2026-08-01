# 🕶️ Приватность

SandForge минимизирует сведения о host:

- не передаёт environment variables автоматически;
- не сохраняет имя пользователя и компьютера в отчёте;
- маскирует чувствительные пути в security findings;
- не читает clipboard, browser profiles или host process list;
- хранит SHA-256 и технические metadata, необходимые для воспроизводимости.

В alpha локальная история находится в `%LocalAppData%\SandForge\sessions\index.json` или в `data\sessions\index.json` при portable mode.
