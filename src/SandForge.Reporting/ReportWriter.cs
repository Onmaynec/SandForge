using System.Net;
using System.Text;
using System.Text.Json;
using SandForge.Domain;

namespace SandForge.Reporting;

public sealed class ReportWriter
{
    public string ToConsole(SandboxSession session)
    {
        var builder = new StringBuilder();
        builder.AppendLine("SANDFORGE SESSION REPORT");
        builder.AppendLine(new string('─', 40));
        builder.AppendLine($"Session:   {session.Id}");
        builder.AppendLine($"Template:  {session.TemplateId}");
        builder.AppendLine($"Status:    {session.Status}");
        builder.AppendLine($"Risk:      {session.Risk}");
        builder.AppendLine($"SHA-256:   {session.TargetFileHash}");
        builder.AppendLine($"Artifacts: {session.Artifacts.Count}");
        if (!string.IsNullOrWhiteSpace(session.Error)) builder.AppendLine($"Error:     {session.Error}");
        return builder.ToString();
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
        string artifacts = string.Join(Environment.NewLine, session.Artifacts.Select(x =>
            $"<tr><td>{E(x.RelativePath)}</td><td>{x.Size:N0}</td><td><code>{E(x.Sha256)}</code></td></tr>"));
        string html = $$"""
        <!doctype html>
        <html lang="ru">
        <head>
          <meta charset="utf-8">
          <meta name="viewport" content="width=device-width,initial-scale=1">
          <title>SandForge — {{E(session.Id)}}</title>
          <style>
            :root{color-scheme:dark;--bg:#08121b;--panel:#102330;--line:#244556;--text:#e7f6ff;--muted:#8eb6c7;--cyan:#48d7e8;--orange:#ff9f43}
            *{box-sizing:border-box}body{margin:0;font:15px/1.55 system-ui;background:linear-gradient(135deg,#08121b,#0c1b27);color:var(--text)}
            main{max-width:1100px;margin:auto;padding:40px 22px}header{border:1px solid var(--line);background:var(--panel);border-radius:18px;padding:28px}
            h1{margin:0;color:var(--cyan)}.tag{display:inline-block;margin-top:10px;padding:5px 10px;border-radius:999px;background:#173747;color:var(--orange)}
            .grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(220px,1fr));gap:14px;margin:18px 0}.card{padding:16px;border:1px solid var(--line);border-radius:14px;background:var(--panel)}
            table{width:100%;border-collapse:collapse;background:var(--panel);border-radius:14px;overflow:hidden}th,td{padding:12px;border-bottom:1px solid var(--line);text-align:left}code{overflow-wrap:anywhere;color:var(--cyan)}
          </style>
        </head>
        <body><main>
          <header><h1>SandForge Session Report</h1><div class="tag">{{E(session.Status.ToString())}}</div><p>Автономный отчёт без внешних ресурсов.</p></header>
          <section class="grid">
            <div class="card"><b>Session</b><br>{{E(session.Id)}}</div>
            <div class="card"><b>Template</b><br>{{E(session.TemplateId)}}</div>
            <div class="card"><b>Risk</b><br>{{E(session.Risk.ToString())}}</div>
            <div class="card"><b>Artifacts</b><br>{{session.Artifacts.Count}}</div>
          </section>
          <section class="card"><b>Target SHA-256</b><br><code>{{E(session.TargetFileHash)}}</code></section>
          <h2>Artifacts</h2>
          <table><thead><tr><th>Path</th><th>Size</th><th>SHA-256</th></tr></thead><tbody>{{artifacts}}</tbody></table>
        </main></body></html>
        """;
        await File.WriteAllTextAsync(outputPath, html, new UTF8Encoding(false), cancellationToken);
        return Path.GetFullPath(outputPath);
    }

    private static string E(string value) => WebUtility.HtmlEncode(value);
}
