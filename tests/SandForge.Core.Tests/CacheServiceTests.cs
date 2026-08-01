using SandForge.Domain;
using SandForge.Core;
using Xunit;

namespace SandForge.Core.Tests;

public sealed class CacheServiceTests
{
    [Fact]
    public async Task DryRunDoesNotDeleteManagedCache()
    {
        string root = Path.Combine(Path.GetTempPath(), "sandforge-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var cache = new CacheService(root);
            string file = Path.Combine(cache.GetHostPath("nuget"), "package.bin");
            await File.WriteAllTextAsync(file, "cache");

            CacheCleanupResult result = await cache.CleanupAsync("nuget", true, CancellationToken.None);

            Assert.Equal(1, result.RemovedEntries);
            Assert.True(File.Exists(file));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void RejectsUnknownCacheType()
    {
        string root = Path.Combine(Path.GetTempPath(), "sandforge-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var cache = new CacheService(root);
            Assert.Throws<InvalidDataException>(() => cache.GetHostPath("secrets"));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
