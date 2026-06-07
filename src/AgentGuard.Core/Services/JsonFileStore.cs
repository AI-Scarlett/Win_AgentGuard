using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

namespace AgentGuard.Core.Services;

public sealed class JsonFileStore
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _appendLocks = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _writeLocks = new(StringComparer.OrdinalIgnoreCase);

    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public static readonly JsonSerializerOptions CompactOptions = new(JsonSerializerDefaults.Web);

    public async Task<T?> ReadAsync<T>(string path, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path))
        {
            return default;
        }

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<T>(stream, Options, cancellationToken);
    }

    public async Task WriteAsync<T>(string path, T value, CancellationToken cancellationToken = default)
    {
        var gate = WriteGate(path);
        await gate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
            var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
            await using (var stream = File.Create(tempPath))
            {
                await JsonSerializer.SerializeAsync(stream, value, Options, cancellationToken);
            }

            ReplaceOrMove(tempPath, path);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task WriteTextAsync(string path, string content, CancellationToken cancellationToken = default)
    {
        var gate = WriteGate(path);
        await gate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
            var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
            await File.WriteAllTextAsync(tempPath, content, cancellationToken);
            ReplaceOrMove(tempPath, path);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task AppendLineAsync(string path, string line, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        await File.AppendAllTextAsync(path, line + Environment.NewLine, cancellationToken);
    }

    public async Task AppendLineWithRotationAsync(
        string path,
        string line,
        long maxBytes,
        int maxArchiveCount,
        CancellationToken cancellationToken = default)
    {
        if (maxBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxBytes));
        }

        var fullPath = Path.GetFullPath(path);
        var gate = _appendLocks.GetOrAdd(fullPath, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
            var bytesToAppend = Encoding.UTF8.GetByteCount(line) + Encoding.UTF8.GetByteCount(Environment.NewLine);
            if (File.Exists(path) && new FileInfo(path).Length + bytesToAppend > maxBytes)
            {
                Rotate(path, maxArchiveCount);
            }

            await File.AppendAllTextAsync(path, line + Environment.NewLine, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    private static void Rotate(string path, int maxArchiveCount)
    {
        if (!File.Exists(path))
        {
            return;
        }

        if (maxArchiveCount <= 0)
        {
            File.Delete(path);
            return;
        }

        var oldest = $"{path}.{maxArchiveCount}";
        if (File.Exists(oldest))
        {
            File.Delete(oldest);
        }

        for (var index = maxArchiveCount - 1; index >= 1; index--)
        {
            var source = $"{path}.{index}";
            if (File.Exists(source))
            {
                File.Move(source, $"{path}.{index + 1}", overwrite: true);
            }
        }

        File.Move(path, $"{path}.1", overwrite: true);
    }

    private SemaphoreSlim WriteGate(string path)
    {
        var fullPath = Path.GetFullPath(path);
        return _writeLocks.GetOrAdd(fullPath, _ => new SemaphoreSlim(1, 1));
    }

    private static void ReplaceOrMove(string tempPath, string path)
    {
        if (File.Exists(path))
        {
            File.Replace(tempPath, path, null);
        }
        else
        {
            File.Move(tempPath, path, overwrite: true);
        }
    }
}
