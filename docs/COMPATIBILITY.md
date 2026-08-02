# Compatibility policy SandForge 0.5.0

SandForge `0.5.0` вводит явные версии для всех форматов, которые пересекают границу процесса, sandbox-сессии, portable package или релиза.

## Зарегистрированные контракты

| Контракт | Текущая версия | Поддерживаемые | Устаревшие | JSON Schema |
|---|---:|---|---|---|
| `template` | 2 | 1, 2 | 1 | `schemas/template.schema.json` |
| `config` | 2 | 2 | — | `schemas/config.schema.json` |
| `report` | 1 | 0, 1 | 0 | `schemas/report.schema.json` |
| `completion-marker` | 1 | 1 | — | `schemas/completion-marker.schema.json` |
| `package-manifest` | 1 | 1 | — | `schemas/package-manifest.schema.json` |

Машинно-читаемый реестр хранится в `schemas/catalog.json`.

## CLI

```text
sandforge schema list
sandforge schema describe template
sandforge schema validate templates/minimal/sandforge.yaml
sandforge schema validate report.json --contract report
```

Результат проверки:

- exit code `0` — документ совместим;
- exit code `2` — неверное использование команды;
- exit code `4` — документ невалиден, контракт неизвестен или версия не поддерживается.

## Правила версионирования

- `schemaVersion` обязателен для всех текущих JSON-контрактов.
- Поддерживаемая устаревшая версия может быть прочитана, но должна выдавать предупреждение.
- Неизвестная или неподдерживаемая версия отклоняется до выполнения, запуска или импорта.
- Добавление необязательного поля считается backward-compatible изменением.
- Удаление или переименование поля, изменение его смысла либо enum value требует новой версии схемы.
- Domain/error codes не зависят от языка и не переводятся.
- Имена JSON-свойств и enum values публичных контрактов фиксируются в lower camel case.
- После `1.0.0` breaking change публичного контракта потребует major release.

## Совместимость шаблонов

Template schema `2` является текущей. Schema `1` остаётся доступной как deprecated-формат и выдаёт предупреждение. Перед запуском шаблон проходит проверку структуры, разрешение безопасных `extends/includes` и security validation.

## Миграция отчётов

Отчёты, созданные до `0.5.0`, не содержат `schemaVersion`, `generatedAt` и `generatorVersion`. Они определяются как report schema `0`, остаются читаемыми в период до `1.0.0` и выдают предупреждение об устаревании.

Новые отчёты используют schema `1` и содержат:

- `schemaVersion`;
- `language`;
- `generatedAt`;
- `generatorVersion`;
- `session`.

## Portable package manifest

`scripts/package.ps1` создаёт `manifest.json` до упаковки ZIP. Manifest содержит:

- версию продукта;
- runtime identifier;
- время создания;
- относительный путь каждого payload-файла;
- размер файла;
- SHA-256.

Пути должны быть относительными и не могут содержать сегменты `..`. Manifest не включает собственный hash.

## Целостность релиза

GitHub Release `v0.5.0` содержит:

- `SandForge-0.5.0-win-x64.zip`;
- `SandForge-0.5.0-win-x64.zip.sha256`.

Checksum и package manifest позволяют проверить целостность, но подтверждают происхождение только при получении через доверенный канал. Криптографическая подпись, fingerprint издателя и trust-chain validation вынесены в задачу [#15](../../issues/15).

## Что остаётся до 1.0.0

Контракты `0.5.0` опубликованы и используются в runtime, но долгосрочная policy ещё должна быть завершена:

- миграции между будущими версиями схем;
- расширенный набор legacy fixtures в CI;
- формальное окно поддержки deprecated-версий;
- правила удаления устаревшей схемы;
- подписанные release/package metadata.

Текущий прогресс отслеживается в [issue #14](../../issues/14), а общий порядок работ — в [ROADMAP.md](ROADMAP.md).
