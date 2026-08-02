# 🌐 Локализация RU/EN

SandForge использует один resource catalog для CLI, TUI и отчётов. Русский язык является neutral fallback.

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

## Отчёты

Console и HTML reports используют выбранный язык. HTML получает корректный атрибут `lang`. JSON report сохраняет `language` рядом с объектом `session`, при этом доменные enum/status значения остаются language-neutral.

## Добавление ключей

Ресурсы находятся в:

- `src/SandForge.Reporting/Resources/Strings.resx` — русский neutral resource;
- `src/SandForge.Reporting/Resources/Strings.en.resx` — английский resource.

Новый обязательный ключ добавляется в оба файла и в `UiText.RequiredKeys`. Тест `LocalizationTests.EveryLanguageContainsAllRequiredKeys` останавливает CI, если ключ отсутствует в одном из языков.

Security finding codes, session statuses и exit codes не переводятся на уровне domain. Перевод выполняется только в presentation layer.
