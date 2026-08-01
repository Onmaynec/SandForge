using SandForge.Domain;
using System.Net;
using System.Text;
using SandForge.Core;
using Xunit;

namespace SandForge.Core.Tests;

public sealed class UpdateServiceTests
{
    [Fact]
    public async Task FindsPreviewReleaseAndRequiredAssets()
    {
        string json = """
        [
          {
            "tag_name": "v0.3.0-alpha",
            "draft": false,
            "prerelease": true,
            "assets": [
              { "name": "SandForge-0.3.0-alpha-win-x64.zip", "browser_download_url": "https://github.com/Onmaynec/SandForge/releases/download/v0.3.0-alpha/SandForge-0.3.0-alpha-win-x64.zip" },
              { "name": "SandForge-0.3.0-alpha-win-x64.zip.sha256", "browser_download_url": "https://github.com/Onmaynec/SandForge/releases/download/v0.3.0-alpha/SandForge-0.3.0-alpha-win-x64.zip.sha256" }
            ]
          }
        ]
        """;
        using var client = new HttpClient(new StaticHandler(json));
        string root = Path.Combine(Path.GetTempPath(), "sandforge-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var service = new UpdateService(root, root, client);
            UpdateCheckResult result = await service.CheckAsync("0.2.0-alpha", new UpdateSettings { Channel = "preview" }, CancellationToken.None);

            Assert.True(result.IsUpdateAvailable);
            Assert.Equal("0.3.0-alpha", result.LatestVersion);
            Assert.NotNull(result.PackageUrl);
            Assert.NotNull(result.ChecksumUrl);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task StableChannelIgnoresPrerelease()
    {
        string json = """
        [
          { "tag_name": "v0.4.0-alpha", "draft": false, "prerelease": true, "assets": [] },
          { "tag_name": "v0.2.0", "draft": false, "prerelease": false, "assets": [] }
        ]
        """;
        using var client = new HttpClient(new StaticHandler(json));
        string root = Path.Combine(Path.GetTempPath(), "sandforge-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var service = new UpdateService(root, root, client);
            UpdateCheckResult result = await service.CheckAsync("0.2.0", new UpdateSettings { Channel = "stable" }, CancellationToken.None);

            Assert.False(result.IsUpdateAvailable);
            Assert.Equal("0.2.0", result.LatestVersion);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private sealed class StaticHandler(string content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(content, Encoding.UTF8, "application/json")
            });
        }
    }
}
