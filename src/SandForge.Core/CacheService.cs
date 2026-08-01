using SandForge.Domain;

namespace SandForge.Core;

public sealed class CacheService
{
    private static readonly IReadOnlyDictionary<string, string> GuestPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["nuget"] = @"C:\Sandbox\Cache\nuget",
        ["npm"] = @"C:\Sandbox\Cache\npm",
        ["pip"] = @"C:\Sandbox\Cache\pip",
        ["winget"] = @"C:\Sandbox\Cache\winget"
    };

    private readonly string _root;

    public CacheService(string dataDirectory)
    {
        _root = Path.Combine(Path.GetFullPath(dataDirectory), "cache");
        Directory.CreateDirectory(_root);
    }

    public static IReadOnlyCollection<string> AllowedTypes => GuestPaths.Keys.ToArray();

    public string GetHostPath(string type)
    {
        ValidateType(type);
        string path = Path.Combine(_root, type.ToLowerInvariant());
        EnsureInside(_root, path);
        Directory.CreateDirectory(path);
        return path;
    }

    public static string GetGuestPath(string type)
    {
        ValidateType(type);
        return GuestPaths[type];
    }

    public Task<IReadOnlyList<CacheEntry>> ListAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = new List<CacheEntry>();
        foreach (string type in AllowedTypes.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            string path = GetHostPath(type);
            DirectoryInfo directory = new(path);
            result.Add(new CacheEntry(type, path, GetSize(directory, cancellationToken), directory.LastWriteTimeUtc));
        }
        return Task.FromResult<IReadOnlyList<CacheEntry>>(result);
    }

    public Task<CacheCleanupResult> CleanupAsync(string? type, bool dryRun, CancellationToken cancellationToken)
    {
        string[] types = string.IsNullOrWhiteSpace(type) ? AllowedTypes.ToArray() : [type];
        int removed = 0;
        long reclaimed = 0;
        foreach (string item in types)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string path = GetHostPath(item);
            foreach (FileInfo file in new DirectoryInfo(path).EnumerateFiles("*", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                reclaimed += file.Length;
                removed++;
                if (!dryRun) file.Delete();
            }
            if (!dryRun) RemoveEmptyDirectories(new DirectoryInfo(path));
        }
        return Task.FromResult(new CacheCleanupResult(removed, reclaimed, dryRun));
    }

    public Task EnforceQuotaAsync(IEnumerable<string> types, int maximumSizeMb, CancellationToken cancellationToken)
    {
        if (maximumSizeMb <= 0) throw new ArgumentOutOfRangeException(nameof(maximumSizeMb));
        long maximumBytes = maximumSizeMb * 1024L * 1024L;
        foreach (string type in types.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string path = GetHostPath(type);
            List<FileInfo> files = new DirectoryInfo(path).EnumerateFiles("*", SearchOption.AllDirectories)
                .OrderBy(x => x.LastWriteTimeUtc)
                .ToList();
            long size = files.Sum(x => x.Length);
            foreach (FileInfo file in files)
            {
                if (size <= maximumBytes) break;
                cancellationToken.ThrowIfCancellationRequested();
                long length = file.Length;
                file.Delete();
                size -= length;
            }
            RemoveEmptyDirectories(new DirectoryInfo(path));
        }
        return Task.CompletedTask;
    }

    private static void ValidateType(string type)
    {
        if (!GuestPaths.ContainsKey(type))
            throw new InvalidDataException($"Неизвестный тип cache: {type}. Допустимо: {string.Join(", ", AllowedTypes)}.");
    }

    private static long GetSize(DirectoryInfo directory, CancellationToken cancellationToken)
    {
        long size = 0;
        foreach (FileInfo file in directory.EnumerateFiles("*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            size += file.Length;
        }
        return size;
    }

    private static void RemoveEmptyDirectories(DirectoryInfo directory)
    {
        foreach (DirectoryInfo child in directory.EnumerateDirectories())
        {
            RemoveEmptyDirectories(child);
            if (!child.EnumerateFileSystemInfos().Any()) child.Delete();
        }
    }

    private static void EnsureInside(string root, string candidate)
    {
        string normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string normalizedCandidate = Path.GetFullPath(candidate);
        if (!normalizedCandidate.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Cache path escaped the SandForge data directory.");
    }
}
