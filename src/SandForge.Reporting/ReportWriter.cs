using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SandForge.Domain;

namespace SandForge.Reporting;

public sealed class ReportWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly UiText _text;
    private readonly string _generatorVersion;

    public ReportWriter(UiText? text = null, string generatorVersion = "0.5.0-alpha")
    {
        _text = text ?? UiText.Russian;
        _generatorVersion = generatorVersion;
    }

    public UiText Text => _text;

    public string ToConsole(SandboxSession session)
    {
        var b = new StringBuilder();
        b.AppendLine(_text["Report_Title"]);
        b.AppendLine(new string('─', 48));
        b.AppendLine($"{_text["Report_Session"],-14}{session.Id}");
        b.AppendLine($"{_text["Report_Template"],-14}{session.TemplateId}");
        b.AppendLine($"{_text["Report_Status"],-14}{_text.Status(session.Status)}");
        b.AppendLine($"{_text["Report_Risk"],-14}{_text.Risk(session.Risk)}");
        b.AppendLine($"{_text["Report_TargetHash"],-14}{session.TargetFileHash}");
        b.AppendLine($"{_text["Report_Artifacts"],-14}{session.Artifacts.Count}");
        b.AppendLine($"{_text["Report_Collectors"],-14}{session.Collectors.Count}");
        b.AppendLine($"{_text["Report_Cleanup"],-14}{_text.Cleanup(session.Cleanup)}");
        foreach (CollectorResult collector in session.Collectors)
        {
            string state = collector.Error is null ? string.Empty : $" — {_text["Report_CollectorError"]}";
            b.AppendLine($"  • {collector.Id,-24} {collector.ItemCount,6}{state}");
        }
        if (!string.IsNullOrWhiteSpace(session.Error))
            b.AppendLine($"{_text["Report_Error"],-14}{session.Error}");
        return b.ToString();
    }

    public async Task<string> WriteJsonAsync(SandboxSession session, string outputPath, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        var document = new SessionReportDocument
        {
            SchemaVersion = 1,
            Language = _text.LanguageCode,
            GeneratedAt = DateTimeOffset.UtcNow,
            GeneratorVersion = _generatorVersion,
            Session = session
        };
        await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(document, JsonOptions), cancellationToken);
        return Path.GetFullPath(outputPath);
    }

    public async Task<string> WriteHtmlAsync(SandboxSession session, string outputPath, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        string artifacts = string.Join(Environment.NewLine, session.Artifacts.Select(x =>
            $"<tr><td>{E(x.Type)}</td><td>{E(x.RelativePath)}</td><td>{x.Size.ToString("N0", _text.Culture)}</td><td><code>{E(x.Sha256)}</code></td></tr>"));
        string collectors = string.Join(Environment.NewLine, session.Collectors.Select(x =>
            $"<tr><td>{E(x.Id)}</td><td>{x.ItemCount.ToString(_text.Culture)}</td><td>{E(x.RelativePath)}</td><td>{E(x.Error ?? _text["Report_Ok"])}</td></tr>"));
        string html = $$"""
        <!doctype html><html lang="{{_text.HtmlLanguage}}"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>SandForge — {{E(session.Id)}}</title>
        <style>:root{color-scheme:dark;--bg:#08121b;--panel:#102330;--line:#244556;--text:#e7f6ff;--muted:#8eb6c7;--cyan:#48d7e8;--orange:#ff9f43}*{box-sizing:border-box}body{margin:0;font:15px/1.55 system-ui;background:linear-gradient(135deg,#08121b,#0c1b27);color:var(--text)}main{max-width:1200px;margin:auto;padding:40px 22px}header,.card,table{border:1px solid var(--line);background:var(--panel);border-radius:16px}header,.card{padding:20px}h1{color:var(--cyan)}.tag{display:inline-block;padding:5px 10px;border-radius:999px;background:#173747;color:var(--orange)}.grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(190px,1fr));gap:14px;margin:18px 0}table{width:100%;border-collapse:collapse;overflow:hidden;margin-bottom:24px}th,td{padding:11px;border-bottom:1px solid var(--line);text-align:left}code{overflow-wrap:anywhere;color:var(--cyan)}</style></head>
        <body><main><header><h1>{{E(_text["Report_Title"])}}</h1><span class="tag">{{E(_text.Status(session.Status))}}</span><p>{{E(_text["Report_Offline"])}}</p></header>
        <section class="grid"><div class="card"><b>{{E(_text["Report_Session"])}}</b><br>{{E(session.Id)}}</div><div class="card"><b>{{E(_text["Report_Template"])}}</b><br>{{E(session.TemplateId)}}</div><div class="card"><b>{{E(_text["Report_Risk"])}}</b><br>{{E(_text.Risk(session.Risk))}}</div><div class="card"><b>{{E(_text["Report_Artifacts"])}}</b><br>{{session.Artifacts.Count}}</div></section>
        <section class="card"><b>{{E(_text["Report_TargetHash"])}}</b><br><code>{{E(session.TargetFileHash)}}</code></section><h2>{{E(_text["Report_Collectors"])}}</h2><table><thead><tr><th>{{E(_text["Report_Collector"])}}</th><th>{{E(_text["Report_Changes"])}}</th><th>{{E(_text["Report_File"])}}</th><th>{{E(_text["Report_State"])}}</th></tr></thead><tbody>{{collectors}}</tbody></table>
        <h2>{{E(_text["Report_Artifacts"])}}</h2><table><thead><tr><th>{{E(_text["Report_Type"])}}</th><th>{{E(_text["Report_Path"])}}</th><th>{{E(_text["Report_Size"])}}</th><th>SHA-256</th></tr></thead><tbody>{{artifacts}}</tbody></table></main></body></html>
        """;
        await File.WriteAllTextAsync(outputPath, html, new UTF8Encoding(false), cancellationToken);
        return Path.GetFullPath(outputPath);
    }

    private static string E(string value) => WebUtility.HtmlEncode(value);
}
