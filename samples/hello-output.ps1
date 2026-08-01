$OutputPath = 'C:\Sandbox\Output\hello-from-sandbox.txt'
"Hello from Windows Sandbox at $([DateTimeOffset]::UtcNow.ToString('O'))" | Set-Content -Encoding UTF8 $OutputPath
