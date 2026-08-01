# 🩺 Устранение неполадок

## Windows Sandbox недоступна

Запусти:

```powershell
sandforge doctor
```

Проверь:

- Windows 10/11 x64 и поддерживаемую редакцию;
- включённую аппаратную виртуализацию в BIOS/UEFI;
- компонент `Containers-DisposableClientVM`;
- наличие перезагрузки после включения компонента.

Включение через PowerShell от администратора:

```powershell
Enable-WindowsOptionalFeature -Online -FeatureName Containers-DisposableClientVM -All
```

## Сессия завершилась timeout

Открой workspace сессии и проверь:

```text
output\.sandforge\bootstrap-error.txt
output\.sandforge\completed.json
```

## Артефакт не импортирован

Файл мог превышать лимит 256 MB, количество артефактов могло превысить 10 000 или файл находился внутри служебной `.sandforge` директории.
