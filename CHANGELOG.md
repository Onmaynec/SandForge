# Changelog

## [0.4.0-alpha] - 2026-08-01

### Добавлено
- полноценный keyboard-first TUI на Spectre.Console;
- dashboard состояния Windows Sandbox и последних сессий;
- мастер запуска с выбором шаблона и предварительным security plan;
- live-статусы подготовки, запуска, выполнения и импорта collectors;
- интерактивные экраны сессий, отчётов, recovery, cleanup, cache и updates;
- общий ресурсный слой локализации для CLI, TUI и отчётов;
- английские ресурсы и режимы `ui.language: ru|en|auto`;
- CI-тест, проверяющий parity обязательных ключей RU/EN.

### Изменено
- запуск без аргументов открывает TUI, а существующие CLI-команды сохранены без изменений сценария вызова;
- console и HTML reports используют выбранный язык;
- JSON report сохраняет код языка вместе с данными сессии;
- прогресс сессии передаётся language-neutral событиями `SessionProgress`;
- версия проекта повышена до `0.4.0-alpha`.

## [0.3.0-alpha] - 2026-08-01

### Добавлено
- безопасные `extends` и `includes` для schemaVersion 2;
- package и local installer provisioning внутри guest;
- Matrix Runner с ограничением параллелизма;
- отдельный managed cache с quota;
- команды `update check/install/auto/channel`;
- GitHub Releases updater с SHA-256, безопасной распаковкой и rollback;
- документация `UPDATES`, `PROVISIONING`, `MATRIX`, `CACHE`.

### Изменено
- версия проекта повышена до `0.3.0-alpha`;
- built-in templates переведены на наследование от общего безопасного base;
- release workflow публикует package и checksum, совместимые с updater.

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
