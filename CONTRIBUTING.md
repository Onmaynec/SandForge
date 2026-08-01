# 🤝 Участие в разработке

Спасибо за интерес к SandForge!

1. Создай отдельную ветку.
2. Не ослабляй security defaults без отдельного обоснования.
3. Добавляй тесты для path handling, шаблонов и policy rules.
4. Запусти:

```powershell
dotnet format --verify-no-changes
dotnet build -c Release
dotnet test -c Release
```

5. В PR опиши угрозы, ограничения и влияние на backward compatibility.

Не добавляй в fixtures реальные вредоносные файлы, секреты или персональные данные.
