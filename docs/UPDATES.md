# 🔄 Обновления SandForge через GitHub

SandForge `0.5.0` использует GitHub Releases как канал доставки опубликованных win-x64 сборок. Текущий стабильный релиз: [v0.5.0](../../releases/tag/v0.5.0).

## Команды

```powershell
sandforge update status
sandforge update check
sandforge update install
sandforge update install --yes
sandforge update auto on
sandforge update auto on --apply
sandforge update auto off
sandforge update channel stable
sandforge update channel preview
```

`update check` возвращает exit code `20`, когда найдена новая версия.

## Assets стабильного релиза

Для версии `0.5.0` публикуются:

- `SandForge-0.5.0-win-x64.zip`;
- `SandForge-0.5.0-win-x64.zip.sha256`.

Внутри ZIP находится `manifest.json` с версией продукта, runtime identifier, относительными путями, размерами и SHA-256 payload-файлов.

## Модель безопасности

1. SandForge запрашивает Releases только по HTTPS через GitHub API.
2. Выбирается подходящий release канала `stable` или `preview`.
3. Требуются два assets: `SandForge-<version>-win-x64.zip` и соответствующий `.sha256`.
4. ZIP принимается только с `github.com` или `*.githubusercontent.com`.
5. SHA-256 вычисляется локально до распаковки.
6. Каждый путь ZIP проверяется на выход за staging-каталог.
7. Package manifest проверяет относительные пути, размеры и hashes payload-файлов.
8. Текущая установка копируется в backup.
9. Замена выполняется отдельным PowerShell-процессом после завершения SandForge.
10. Новая версия запускается с `--version` как self-check.
11. При ошибке выполняется rollback из backup.

> [!IMPORTANT]
> SHA-256 и manifest подтверждают целостность только при получении через доверенный канал. Криптографическая подпись и trust-chain validation запланированы для `0.6.0` в [issue #15](../../issues/15).

## Пользовательские данные

Обновление заменяет только файлы установленной программы. Пользовательские `config`, `sessions`, отчёты и управляемый `cache` хранятся вне каталога приложения и не перезаписываются.

Автоматическая проверка включена по умолчанию раз в 24 часа. Автоматическая установка выключена и включается только командой:

```powershell
sandforge update auto on --apply
```

Она применяется при запуске интерактивного меню и не прерывает произвольную CLI-команду.

## Ручная проверка скачанного архива

```powershell
Get-FileHash .\SandForge-0.5.0-win-x64.zip -Algorithm SHA256
Get-Content .\SandForge-0.5.0-win-x64.zip.sha256
```

Полученные значения должны совпадать до распаковки архива.
