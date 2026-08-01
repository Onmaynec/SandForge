# 🔐 Модель безопасности

SandForge использует безопасные значения по умолчанию и оценивает план до запуска.

## Defaults

- сеть выключена;
- clipboard выключен;
- input копируется в session workspace;
- output создаётся отдельно;
- protected client включён;
- timeout обязателен;
- artifacts не открываются автоматически.

## Уровни риска

- **Low** — offline, clipboard disabled, isolated output;
- **Medium** — network/clipboard enabled;
- **High** — writable host mount или чрезмерный timeout;
- **Critical** — writable system drive, profile/credential locations; запуск блокируется.

## Реализованные проверки

- нормализация host paths;
- блокировка root и чувствительных writable mounts;
- SHA-256 streaming;
- path traversal protection;
- artifact count/size quotas;
- HTML escaping;
- safe process arguments через `ArgumentList` в guest bootstrap;
- completion marker validation;
- отсутствие автоматического запуска артефактов.

## Не является гарантией

SandForge не является антивирусом, EDR или полноценной malware analysis platform. Windows Sandbox и сам SandForge могут содержать уязвимости. Для высокорисковых образцов используйте специально подготовленную VM и профессиональные средства анализа.
