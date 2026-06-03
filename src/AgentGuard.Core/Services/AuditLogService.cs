using AgentGuard.Core.Models;

namespace AgentGuard.Core.Services;

public sealed class AuditLogService
{
    private readonly JsonFileStore _store;
    private readonly AppPaths _paths;
    private readonly GuardAnalyzer _analyzer;
    private readonly object _gate = new();

    public List<OperationRecord> Records { get; private set; } = [];

    public event EventHandler? Changed;

    public AuditLogService(JsonFileStore store, AppPaths paths, GuardAnalyzer analyzer)
    {
        _store = store;
        _paths = paths;
        _analyzer = analyzer;
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        Records = await _store.ReadAsync<List<OperationRecord>>(_paths.AuditRecordsPath, cancellationToken) ?? [];
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public Task SaveAsync(CancellationToken cancellationToken = default)
    {
        List<OperationRecord> snapshot;
        lock (_gate)
        {
            snapshot = Records.Take(5000).ToList();
        }

        return _store.WriteAsync(_paths.AuditRecordsPath, snapshot, cancellationToken);
    }

    public void Ingest(OperationRecord record)
    {
        lock (_gate)
        {
            Records.Insert(0, record);
            if (Records.Count > 5000)
            {
                Records.RemoveRange(5000, Records.Count - 5000);
            }
        }

        _analyzer.Analyze(record);
        Changed?.Invoke(this, EventArgs.Empty);
        _ = SaveAsync();
    }

    public List<OperationRecord> Snapshot(int take = 500)
    {
        lock (_gate)
        {
            return Records.Take(take).ToList();
        }
    }
}
