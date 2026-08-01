# 🧪 Matrix Runner

Matrix Runner запускает один целевой файл по нескольким шаблонам и сохраняет независимую сессию для каждого запуска.

```powershell
sandforge matrix run .\Application.exe --templates minimal,isolated-analysis,installer-test
sandforge matrix run .\Application.exe --templates .\templates\a\sandforge.yaml,.\templates\b\sandforge.yaml --parallel 2
```

Параллелизм по умолчанию равен `1` и ограничен значением `4`. Ошибка одного шаблона не повреждает остальные сессии. Итоговая таблица показывает template, status и session ID.
