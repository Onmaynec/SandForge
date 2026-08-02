# 🗺️ Roadmap SandForge

Актуальная стабильная версия: **`0.5.0`**. Сборка и checksum опубликованы в [GitHub Release v0.5.0](https://github.com/Onmaynec/SandForge/releases/tag/v0.5.0).

## ✅ 0.1.0-alpha — Безопасный сквозной сценарий

- [x] доменные модели и контракты;
- [x] ограниченный безопасный Template Engine;
- [x] Security Policy Engine;
- [x] session workspace;
- [x] генерация `.wsb` и запуск Windows Sandbox;
- [x] guest bootstrap и completion marker;
- [x] импорт user-output с SHA-256;
- [x] console/JSON/HTML-отчёты;
- [x] CI и portable packaging.

## ✅ 0.2.0-alpha — Видимость изменений установщика

- [x] SQLite-хранилище и схема миграций;
- [x] автоматический импорт истории `index.json`;
- [x] process и installed-app collectors;
- [x] file/registry/service/scheduled-task snapshots;
- [x] before/after JSON-diff;
- [x] команда `test-installer`;
- [x] отчёт по результатам коллекторов;
- [x] crash recovery;
- [x] cleanup и dry-run;
- [x] русская документация и CLI по умолчанию.

## ✅ 0.3.0-alpha — Воспроизводимые среды разработки

- [x] package provisioning внутри Sandbox;
- [x] includes и наследование шаблонов;
- [x] matrix runner;
- [x] управляемый cache;
- [x] безопасные обновления через GitHub Releases.

## ✅ 0.4.0-alpha — Интерактивное управление и локализация

- [x] Spectre.Console dashboard и keyboard-first navigation;
- [x] мастер запуска с security plan и подтверждением опасных настроек;
- [x] live host lifecycle progress;
- [x] экраны sessions/reports/recovery/cleanup/cache/updates;
- [x] общий RU/EN resource catalog;
- [x] `ui.language: ru|en|auto` и русский fallback;
- [x] CI-проверка parity обязательных ключей локализации;
- [ ] локализация оставшихся сообщений Core/guest bootstrap без изменения domain codes — [#13](https://github.com/Onmaynec/SandForge/issues/13).

## ✅ 0.5.0 — Compatibility contracts

- [x] versioned registry публичных форматов;
- [x] JSON Schema Draft 2020-12 и общий catalog;
- [x] команда `schema list|describe|validate`;
- [x] supported/deprecated version policy;
- [x] versioned JSON reports;
- [x] portable package manifest с SHA-256;
- [x] contract tests для текущих и legacy-форматов;
- [x] стабильный тег `v0.5.0`, Windows x64 ZIP и checksum;
- [ ] миграции между будущими версиями схем и долгосрочное окно поддержки — [#14](https://github.com/Onmaynec/SandForge/issues/14).

## 🚧 0.6.0 — Trust model и подписи

- [x] автоматическая публикация ZIP и SHA-256 checksum;
- [ ] подписанный manifest для `.sftemplate`;
- [ ] fingerprint автора/издателя;
- [ ] статусы `Trusted`, `Modified`, `Untrusted`, `Unsigned`;
- [ ] безопасный просмотр неподписанного package без выполнения;
- [ ] подпись release metadata и документация trust model.

Задача: [#15 — Добавить подпись шаблонов и релизов](https://github.com/Onmaynec/SandForge/issues/15).

## 🎯 1.0.0 — Стабильная платформа

- [~] окончательно зафиксировать compatibility policy и миграции — [#14](https://github.com/Onmaynec/SandForge/issues/14);
- [ ] завершить RU/EN локализацию Core/guest — [#13](https://github.com/Onmaynec/SandForge/issues/13);
- [ ] Collector SDK — [#16](https://github.com/Onmaynec/SandForge/issues/16);
- [ ] несколько sandbox backends — [#17](https://github.com/Onmaynec/SandForge/issues/17);
- [ ] внешний security review — [#18](https://github.com/Onmaynec/SandForge/issues/18).

## Порядок работ

1. `0.6.0`: trust model и подписи;
2. завершение локализации Core/guest;
3. стабильные schema migrations и расширенные legacy fixtures;
4. Collector SDK и backend abstraction;
5. внешний security review и подготовка `1.0.0`.

Каждый незавершённый крупный пункт представлен отдельной задачей в GitHub Issues с актуальным статусом и критериями готовности.
