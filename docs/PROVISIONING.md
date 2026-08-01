# 📦 Provisioning внутри Windows Sandbox

Provisioning выполняется только внутри guest до снятия before-snapshot и до запуска целевого файла.

## Package provisioning

```yaml
schemaVersion: 2
sandbox:
  network: enabled
provisioning:
  failurePolicy: stop
  packages:
    - id: Git.Git
      version: "2.50.0"
      source: winget
```

Поддерживается только источник `winget`. Если packages заданы при `sandbox.network: disabled`, security policy блокирует запуск. Незакреплённая версия создаёт предупреждение риска.

## Локальные installers

```yaml
provisioning:
  installers:
    - path: "./payload/tool.msi"
      sha256: "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF"
      timeout: 10m
      arguments:
        - "PROPERTY=value"
```

Путь разрешается относительно файла шаблона. Installer копируется в workspace, его SHA-256 повторно проверяется host-процессом и guest bootstrap. MSI запускается через `msiexec /i /qn /norestart`; EXE запускается напрямую.

Результат сохраняется как collector `provisioning.json` со статусом, exit code и путями stdout/stderr. `failurePolicy: stop` не запускает target при ошибке provisioning; `continue` запускает target и оставляет частичный результат в отчёте.
