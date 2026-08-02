# 🌐 Локализация RU/EN в SandForge 0.5.0

SandForge использует общий resource catalog для CLI, TUI и отчётов. Русский язык является neutral fallback.

## Настройка

В `sandforge.json`:

```json
{
  "ui": {
    "language": "ru"
  }
}
```

Поддерживаемые значения:

- `ru` — русский язык;
- `en` — английский язык;
- `auto` — английский для английской UI-culture ОС, русский для остальных локалей.

Временное переопределение без изменения файла:

```powershell
$env:SANDFORGE_LANGUAGE = 'en'
sandforge
```

## Покрытие 0.5.0

Локализованы:

- CLI и основная справка;
- Spectre.Console TUI;
- security plan и lifecycle progress;
- console и HTML reports;
- schema CLI `list|describe|validate`;
- основные validation warnings и ошибки presentation layer.

Часть низкоуровневых Core/guest bootstrap diagnostics пока может оставаться русской. Завершение покрытия отслеживается в [issue #13](../../issues/13).

## Отчёты

Console и HTML reports используют выбранный язык. HTML получает корректный атрибут `lang`.

JSON report schema `1` содержит:

- `schemaVersion`;
- `language`;
- `generatedAt`;
- `generatorVersion`;
- `session`.

Доменные enum/status значения и error codes остаются language-neutral.

## Добавление ключей

Ресурсы находятся в:

- `src/SandForge.Reporting/Resources/Strings.resx` — русский neutral resource;
- `src/SandForge.Reporting/Resources/Strings.en.resx` — английский resource.

Новый обязательный ключ добавляется в оба файла и в `UiText.RequiredKeys`. Тест `LocalizationTests.EveryLanguageContainsAllRequiredKeys` останавливает CI, если ключ отсутствует в одном из языков.

Security finding codes, session statuses, schema identifiers и exit codes не переводятся на уровне domain. Перевод выполняется только в presentation layer.

## Критерий полного покрытия

Перед `1.0.0` сценарий `ui.language: en` должен полностью проходить TUI → запуск → collectors → report без русских пользовательских сообщений. Технические machine-readable коды при этом не меняются.
