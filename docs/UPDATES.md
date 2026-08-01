# 🔄 Обновления SandForge через GitHub

SandForge `0.3.0-alpha` использует GitHub Releases как доверенный канал доставки опубликованных win-x64 сборок.

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

## Модель безопасности

1. SandForge запрашивает список Releases только по HTTPS через GitHub API.
2. Выбирается подходящий release канала `stable` или `preview`.
3. Требуются два assets: `SandForge-<version>-win-x64.zip` и соответствующий `.sha256`.
4. ZIP принимается только с `github.com` или `*.githubusercontent.com`.
5. SHA-256 вычисляется локально до распаковки.
6. Каждый путь ZIP проверяется на выход за staging-каталог.
7. Текущая установка копируется в backup.
8. Замена выполняется отдельным PowerShell-процессом после завершения SandForge.
9. Новая версия запускается с `--version` как self-check.
10. При ошибке выполняется rollback из backup.

## Пользовательские данные

Обновление заменяет только файлы установленной программы. Пользовательские `config`, `sessions`, отчёты и управляемый `cache` хранятся вне каталога приложения и не перезаписываются.

Автоматическая проверка включена по умолчанию раз в 24 часа. Автоматическая установка выключена и включается только командой:

```powershell
sandforge update auto on --apply
```

Она применяется при запуске интерактивного меню и не прерывает произвольную CLI-команду.
