# История изменений

## [0.2.0-alpha] — 2026-08-01

### Добавлено

- SQLite SessionStore и таблица миграций ([#2](https://github.com/Onmaynec/SandForge/issues/2));
- автоматический перенос истории из `sessions/index.json`;
- команда `test-installer` и шаблон `installer-test` ([#5](https://github.com/Onmaynec/SandForge/issues/5));
- process, installed-app, file, registry, service и scheduled-task collectors ([#3](https://github.com/Onmaynec/SandForge/issues/3), [#4](https://github.com/Onmaynec/SandForge/issues/4));
- before/after JSON-diff внутри guest;
- отображение collector results в console/JSON/HTML;
- команды `recover` и `cleanup` с dry-run ([#6](https://github.com/Onmaynec/SandForge/issues/6));
- русская документация, CLI и отчёты по умолчанию ([#7](https://github.com/Onmaynec/SandForge/issues/7));
- тесты SQLite, recovery, collector payload и cleanup;
- проверка workspace path перед очисткой или удалением;
- независимое выполнение collectors и импорт частичных результатов при timeout.

### Изменено

- `README.md` стал основной русской документацией;
- локальная история перенесена с JSON на SQLite;
- версия CLI обновлена до `0.2.0-alpha`.

## [0.1.0-alpha] — 2026-08-01

- архитектурное ядро и доменные контракты;
- безопасный Template Engine;
- Session Planner и Security Policy Engine;
- workspace, `.wsb` и guest bootstrap;
- completion marker, SHA-256 и импорт user-output;
- CLI, отчёты, тесты и GitHub Actions.
