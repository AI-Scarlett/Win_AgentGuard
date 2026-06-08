using System.Diagnostics;
using System.Runtime.InteropServices;
using AgentGuard.Core.Models;

namespace AgentGuard.Core.Services;

public sealed class WindowsOperationMonitor : IDisposable
{
    private readonly AuditLogService _auditLog;
    private readonly GuardAnalyzer _analyzer;
    private readonly AppPaths _paths;
    private readonly List<FileSystemWatcher> _watchers = [];
    private readonly Dictionary<int, string> _knownAgentProcesses = [];
    private readonly object _gate = new();
    /// <summary>
    /// v1.7.3-style ppid-chain cache: maps process id -> the agent that
    /// ultimately owns the process via the parent chain (max depth 10).
    /// 15s TTL, capped at 5000 entries.
    /// </summary>
    private readonly Dictionary<int, (string Agent, DateTimeOffset CachedAt)> _processTreeCache = [];
    private const int ProcessTreeCacheTtlSeconds = 15;
    private const int ProcessTreeCacheMaxEntries = 5000;
    private const int ProcessTreeMaxDepth = 10;
    private PeriodicTimer? _processTimer;
    private CancellationTokenSource? _cts;
    private Task? _processTask;

    public bool IsMonitoring { get; private set; }
    public HashSet<string> ActiveAgentNames { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<ProcessLifecycleEvent> ProcessEvents { get; } = [];

    public event EventHandler? Changed;

    public WindowsOperationMonitor(AuditLogService auditLog, GuardAnalyzer analyzer, AppPaths paths)
    {
        _auditLog = auditLog;
        _analyzer = analyzer;
        _paths = paths;
    }

    public void Start(IEnumerable<string> watchDirectories)
    {
        if (IsMonitoring)
        {
            return;
        }

        _cts = new CancellationTokenSource();
        StartFileWatchers(watchDirectories);
        _processTimer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        _processTask = Task.Run(() => ProcessLoopAsync(_cts.Token));
        IsMonitoring = true;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task StopAsync()
    {
        if (!IsMonitoring)
        {
            return;
        }

        _cts?.Cancel();
        if (_processTask is not null)
        {
            try
            {
                await _processTask.WaitAsync(TimeSpan.FromSeconds(2));
            }
            catch
            {
                // Cancellation is expected.
            }
        }

        foreach (var watcher in _watchers)
        {
            watcher.Dispose();
        }
        _watchers.Clear();
        _processTimer?.Dispose();
        _cts?.Dispose();
        _cts = null;
        _processTimer = null;
        _processTask = null;
        IsMonitoring = false;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void UpdateWatchDirectories(IEnumerable<string> watchDirectories)
    {
        if (!IsMonitoring)
        {
            return;
        }

        foreach (var watcher in _watchers)
        {
            watcher.Dispose();
        }

        _watchers.Clear();
        StartFileWatchers(watchDirectories);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        _ = StopAsync();
    }

    private void StartFileWatchers(IEnumerable<string> watchDirectories)
    {
        foreach (var directory in watchDirectories.Select(_paths.ExpandUserPath).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(directory))
            {
                continue;
            }

            try
            {
                var watcher = new FileSystemWatcher(directory)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName |
                                   NotifyFilters.DirectoryName |
                                   NotifyFilters.LastWrite |
                                   NotifyFilters.CreationTime |
                                   NotifyFilters.Size
                };
                watcher.Created += (_, args) => RecordFileEvent(args.FullPath, OperationType.Create, "Created");
                watcher.Changed += (_, args) => RecordFileEvent(args.FullPath, OperationType.Modify, "Changed");
                watcher.Deleted += (_, args) => RecordFileEvent(args.FullPath, OperationType.Delete, "Deleted");
                watcher.Renamed += (_, args) => RecordFileEvent(args.FullPath, OperationType.Rename, $"Renamed from {args.OldFullPath}");
                watcher.Error += (_, args) => RecordMonitorError(directory, args.GetException());
                watcher.EnableRaisingEvents = true;
                _watchers.Add(watcher);
            }
            catch (Exception ex)
            {
                RecordMonitorError(directory, ex);
            }
        }
    }

    private async Task ProcessLoopAsync(CancellationToken cancellationToken)
    {
        await ScanProcessesAsync();
        while (_processTimer is not null && await _processTimer.WaitForNextTickAsync(cancellationToken))
        {
            await ScanProcessesAsync();
        }
    }

    private Task ScanProcessesAsync()
    {
        var currentAgents = new Dictionary<int, string>();
        var parentMap = new Dictionary<int, int>();
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                var agentName = AgentCatalog.ResolveAgentName(process.ProcessName);
                if (!string.IsNullOrWhiteSpace(agentName))
                {
                    currentAgents[process.Id] = agentName;
                }
                try
                {
                    parentMap[process.Id] = GetParentProcessId(process);
                }
                catch
                {
                    // Some processes exit while we are inspecting; ignore.
                }
            }
            catch
            {
                // Some processes exit while being inspected.
            }
            finally
            {
                process.Dispose();
            }
        }

        lock (_gate)
        {
            var newPids = currentAgents.Keys.Except(_knownAgentProcesses.Keys).ToList();
            var exitedPids = _knownAgentProcesses.Keys.Except(currentAgents.Keys).ToList();

            foreach (var pid in newPids)
            {
                var agentName = currentAgents[pid];
                _knownAgentProcesses[pid] = agentName;
                ActiveAgentNames.Add(agentName);
                var parentPid = parentMap.TryGetValue(pid, out var pp) ? pp : 0;
                var parentName = parentPid > 0 && currentAgents.TryGetValue(parentPid, out var pn)
                    ? pn
                    : (parentMap.TryGetValue(parentPid, out var _) ? TryGetProcessName(parentPid) : string.Empty);
                var lifecycle = new ProcessLifecycleEvent
                {
                    EventType = "launch",
                    ProcessId = pid,
                    ParentProcessId = parentPid,
                    AgentName = agentName,
                    ProcessName = agentName,
                    Arguments = parentName
                };
                ProcessEvents.Insert(0, lifecycle);
                Trim(ProcessEvents, 1000);
                _analyzer.CheckProcessLifecycle(lifecycle);
            }

            foreach (var pid in exitedPids)
            {
                var agentName = _knownAgentProcesses[pid];
                _knownAgentProcesses.Remove(pid);
                var parentPid = parentMap.TryGetValue(pid, out var pp) ? pp : 0;
                var lifecycle = new ProcessLifecycleEvent
                {
                    EventType = "exit",
                    ProcessId = pid,
                    ParentProcessId = parentPid,
                    AgentName = agentName,
                    ProcessName = agentName
                };
                ProcessEvents.Insert(0, lifecycle);
                Trim(ProcessEvents, 1000);
            }

            ActiveAgentNames.Clear();
            foreach (var agent in _knownAgentProcesses.Values.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                ActiveAgentNames.Add(agent);
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    private static int GetParentProcessId(Process process)
    {
        if (!OperatingSystem.IsWindows())
        {
            return 0;
        }
        try
        {
            var pbi = new ProcessBasicInformation();
            int returnLength;
            int status = NtQueryInformationProcess(
                process.Handle,
                0, // ProcessBasicInformation
                ref pbi,
                Marshal.SizeOf<ProcessBasicInformation>(),
                out returnLength);
            if (status == 0)
            {
                return pbi.InheritedFromUniqueProcessId.ToInt32();
            }
        }
        catch
        {
            // Some processes cannot be inspected (system, protected, exited).
        }
        return 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessBasicInformation
    {
        public IntPtr Reserved1;
        public IntPtr PebBaseAddress;
        public IntPtr Reserved2_0;
        public IntPtr Reserved2_1;
        public IntPtr UniqueProcessId;
        public IntPtr InheritedFromUniqueProcessId;
    }

    [DllImport("ntdll.dll", SetLastError = true)]
    private static extern int NtQueryInformationProcess(
        IntPtr processHandle,
        int processInformationClass,
        ref ProcessBasicInformation processInformation,
        int processInformationLength,
        out int returnLength);

    private static string TryGetProcessName(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            return p.ProcessName;
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// v1.7.3 process-tree attribution. Walks the ppid chain up to
    /// <see cref="ProcessTreeMaxDepth"/> levels looking for an ancestor whose
    /// name resolves to a known agent. Results are cached for 15s so
    /// repeated lookups for hot processes (e.g. node, python spawned by
    /// Claude Code) stay O(1).
    /// </summary>
    public string ResolveAgentViaProcessTree(int pid, IDictionary<int, int> parentMap)
    {
        if (pid <= 0) return string.Empty;
        var now = DateTimeOffset.UtcNow;
        if (_processTreeCache.TryGetValue(pid, out var cached) &&
            (now - cached.CachedAt).TotalSeconds < ProcessTreeCacheTtlSeconds)
        {
            return cached.Agent;
        }

        var visited = new HashSet<int> { pid };
        var current = pid;
        string? resolved = null;
        for (var depth = 0; depth < ProcessTreeMaxDepth && current > 0; depth++)
        {
            var name = TryGetProcessName(current);
            if (!string.IsNullOrEmpty(name))
            {
                var agent = AgentCatalog.ResolveAgentName(name);
                if (!string.IsNullOrWhiteSpace(agent))
                {
                    resolved = agent;
                    break;
                }
            }
            if (!parentMap.TryGetValue(current, out var pp) || pp <= 0) break;
            if (!visited.Add(pp)) break;
            current = pp;
        }

        if (_processTreeCache.Count >= ProcessTreeCacheMaxEntries)
        {
            // Drop the oldest quarter so we don't grow forever.
            var doomed = _processTreeCache
                .OrderBy(kv => kv.Value.CachedAt)
                .Take(ProcessTreeCacheMaxEntries / 4)
                .Select(kv => kv.Key)
                .ToList();
            foreach (var k in doomed) _processTreeCache.Remove(k);
        }
        var agentName = resolved ?? string.Empty;
        _processTreeCache[pid] = (agentName, now);
        return agentName;
    }

    private void RecordFileEvent(string path, OperationType operationType, string detail)
    {
        if (ShouldSkipPath(path))
        {
            return;
        }

        var record = new OperationRecord
        {
            Timestamp = DateTimeOffset.Now,
            AgentName = ActiveAgentNames.Count > 0 ? string.Join(", ", ActiveAgentNames.Take(3)) : "Windows Monitor",
            OperationType = operationType,
            TargetPath = path,
            Detail = detail,
            FileSize = TryFileSize(path),
            ProcessName = "FileSystemWatcher",
            ToolInfo = "Local Windows file watcher"
        };
        _auditLog.Ingest(record);
    }

    private void RecordMonitorError(string directory, Exception exception)
    {
        _auditLog.Ingest(new OperationRecord
        {
            Timestamp = DateTimeOffset.Now,
            AgentName = "Windows Monitor",
            OperationType = OperationType.Read,
            TargetPath = directory,
            Detail = exception.Message,
            ProcessName = "FileSystemWatcher",
            ToolInfo = "Watcher error"
        });
    }

    private static bool ShouldSkipPath(string path)
    {
        var lower = path.ToLowerInvariant();
        return lower.Contains("\\.git\\", StringComparison.Ordinal) ||
               lower.Contains("\\node_modules\\", StringComparison.Ordinal) ||
               lower.Contains("\\bin\\", StringComparison.Ordinal) ||
               lower.Contains("\\obj\\", StringComparison.Ordinal) ||
               lower.EndsWith(".tmp", StringComparison.Ordinal) ||
               lower.EndsWith(".log", StringComparison.Ordinal);
    }

    private static long TryFileSize(string path)
    {
        try
        {
            return File.Exists(path) ? new FileInfo(path).Length : 0;
        }
        catch
        {
            return 0;
        }
    }

    private static void Trim<T>(List<T> list, int max)
    {
        if (list.Count > max)
        {
            list.RemoveRange(max, list.Count - max);
        }
    }
}
