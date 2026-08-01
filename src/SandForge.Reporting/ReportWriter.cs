using System.Net;
using System.Text;
using System.Text.Json;
using SandForge.Domain;

namespace SandForge.Reporting;

public sealed class ReportWriter
{
    public string ToConsole(SandboxSession session)
    {
        var b = new StringBuilder();
        b.AppendLine("ОТЧЁТ СЕССИИ SANDFORGE"); b.AppendLine(new string('─', 48));
        b.AppendLine($"Сессия:       {session.Id}"); b.AppendLine($"Шаблон:       {session.TemplateId}");
        b.AppendLine($"Статус:       {StatusRu(session.Status)}"); b.AppendLine($"Риск:         {RiskRu(session.Risk)}");
        b.AppendLine($"SHA-256:      {session.TargetFileHash}"); b.AppendLine($"Артефакты:    {session.Artifacts.Count}");
        b.AppendLine($"Коллекторы:   {session.Collectors.Count}"); b.AppendLine($"Очистка:      {CleanupRu(session.Cleanup)}");
        foreach (CollectorResult collector in session.Collectors) b.AppendLine($"  • {collector.Id,-24} {collector.ItemCount,6} изменений{(collector.Error is null ? "" : " — ошибка")}");
        if (!string.IsNullOrWhiteSpace(session.Error)) b.AppendLine($"Ошибка:        {session.Error}");
        return b.ToString();
    }

    public async Task<string> WriteJsonAsync(SandboxSession session, string outputPath, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(session, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
        return Path.GetFullPath(outputPath);
    }

    public async Task<string> WriteHtmlAsync(SandboxSession session, string outputPath, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        string artifacts = string.Join(Environment.NewLine, session.Artifacts.Select(x => $"<tr><td>{E(x.Type)}</td><td>{E(x.RelativePath)}</td><td>{x.Size:N0}</td><td><code>{E(x.Sha256)}</code></td></tr>"));
        string collectors = string.Join(Environment.NewLine, session.Collectors.Select(x => $"<tr><td>{E(x.Id)}</td><td>{x.ItemCount}</td><td>{E(x.RelativePath)}</td><td>{E(x.Error ?? "OK")}</td></tr>"));
        string html = $$"""
        <!doctype html><html lang="ru"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>SandForge — {{E(session.Id)}}</title>
        <style>:root{color-scheme:dark;--bg:#08121b;--panel:#102330;--line:#244556;--text:#e7f6ff;--muted:#8eb6c7;--cyan:#48d7e8;--orange:#ff9f43}*{box-sizing:border-box}body{margin:0;font:15px/1.55 system-ui;background:linear-gradient(135deg,#08121b,#0c1b27);color:var(--text)}main{max-width:1200px;margin:auto;padding:40px 22px}header,.card,table{border:1px solid var(--line);background:var(--panel);border-radius:16px}header,.card{padding:20px}h1{color:var(--cyan)}.tag{display:inline-block;padding:5px 10px;border-radius:999px;background:#173747;color:var(--orange)}.grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(190px,1fr));gap:14px;margin:18px 0}table{width:100%;border-collapse:collapse;overflow:hidden;margin-bottom:24px}th,td{padding:11px;border-bottom:1px solid var(--line);text-align:left}code{overflow-wrap:anywhere;color:var(--cyan)}</style></head>
        <body><main><header><h1>Отчёт SandForge</h1><span class="tag">{{E(StatusRu(session.Status))}}</span><p>Автономный отчёт без внешних ресурсов.</p></header>
        <section class="grid"><div class="card"><b>Сессия</b><br>{{E(session.Id)}}</div><div class="card"><b>Шаблон</b><br>{{E(session.TemplateId)}}</div><div class="card"><b>Риск</b><br>{{E(RiskRu(session.Risk))}}</div><div class="card"><b>Артефакты</b><br>{{session.Artifacts.Count}}</div></section>
        <section class="card"><b>SHA-256 цели</b><br><code>{{E(session.TargetFileHash)}}</code></section><h2>Результаты коллекторов</h2><table><thead><tr><th>Коллектор</th><th>Изменения</th><th>Файл</th><th>Состояние</th></tr></thead><tbody>{{collectors}}</tbody></table>
        <h2>Артефакты</h2><table><thead><tr><th>Тип</th><th>Путь</th><th>Размер</th><th>SHA-256</th></tr></thead><tbody>{{artifacts}}</tbody></table></main></body></html>
        """;
        await File.WriteAllTextAsync(outputPath, html, new UTF8Encoding(false), cancellationToken);
        return Path.GetFullPath(outputPath);
    }
    private static string StatusRu(SessionStatus value) => value switch
    {
        SessionStatus.Created => "Создана", SessionStatus.Validating => "Проверка", SessionStatus.Preparing => "Подготовка",
        SessionStatus.Ready => "Готова", SessionStatus.Starting => "Запуск", SessionStatus.Running => "Работает",
        SessionStatus.Stopping => "Остановка", SessionStatus.Collecting => "Сбор данных", SessionStatus.Completed => "Завершена",
        SessionStatus.Partial => "Частично", SessionStatus.Failed => "Ошибка", SessionStatus.Cancelled => "Отменена",
        SessionStatus.TimedOut => "Timeout", SessionStatus.Orphaned => "Потеряна", _ => value.ToString()
    };
    private static string RiskRu(RiskLevel value) => value switch { RiskLevel.Low => "Низкий", RiskLevel.Medium => "Средний", RiskLevel.High => "Высокий", RiskLevel.Critical => "Критический", _ => value.ToString() };
    private static string CleanupRu(CleanupState value) => value switch { CleanupState.Pending => "Ожидает", CleanupState.Kept => "Сохранён", CleanupState.Cleaned => "Очищен", _ => value.ToString() };
    private static string E(string value) => WebUtility.HtmlEncode(value);
}
