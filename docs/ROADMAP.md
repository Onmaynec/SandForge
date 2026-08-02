# 🗺️ Roadmap SandForge

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
- [x] CI-проверка parity ключей локализации;
- [ ] локализация оставшихся сообщений Core/guest bootstrap без изменения domain codes.

## 🚧 0.5.0-alpha — Compatibility contracts

- [x] versioned registry публичных форматов;
- [x] JSON Schema Draft 2020-12 и общий catalog;
- [x] команда `schema list|describe|validate`;
- [x] supported/deprecated version policy;
- [x] versioned JSON reports;
- [x] portable package manifest с SHA-256;
- [x] contract tests для текущих и legacy fixtures;
- [ ] миграции между будущими версиями схем.

## 🎯 1.0.0 — Стабильная платформа

- [~] стабильные схемы и compatibility policy;
- [ ] подпись шаблонов и релизов;
- [ ] Collector SDK;
- [ ] несколько sandbox backends;
- [ ] внешний security review.

Каждый крупный пункт roadmap представлен отдельной задачей в GitHub Issues с критериями готовности.
