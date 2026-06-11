using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using AgentGuard.App.Diagnostics;
using AgentGuard.App.Localization;
using AgentGuard.Core.Models;
using AgentGuard.Core.Services;

namespace AgentGuard.App.ViewModels;

public sealed class MainViewModel : ObservableObject, IAsyncDisposable
{
    private readonly AppPaths _paths = new();
    private readonly JsonFileStore _store = new();
    private readonly SessionStore _sessionStore;
    private readonly GuardAnalyzer _guardAnalyzer;
    private readonly AuditLogService _auditLog;
    private readonly HookInstaller _hookInstaller;
    private readonly AgentRegistryService _agentRegistry;
    private readonly HookServer _hookServer;
    private readonly WindowsOperationMonitor _operationMonitor;
    private readonly AgentSessionScanner _historyScanner;
    private readonly INotificationService _notificationService;
    private AgentSettings _settings = new();

    private PendingHookRequest? _selectedPendingRequest;
    private AgentAdapterState? _selectedAgent;
    private string _statusMessage = AppText.StartingAgentGuard;
    private string _questionAnswer = "";
    private string _responseReason = "";
    private string _newProtectedDirectory = "";
    private string _historyStatusMessage = "";
    private AgentSessionSummary? _selectedSession;
    private int _historyDetailCount;
    private List<AgentHistoryRecord> _allHistoryRecords = [];

    public ObservableCollection<SessionState> Sessions { get; } = [];
    public ObservableCollection<PendingHookRequest> PendingRequests { get; } = [];
    public ObservableCollection<OperationRecord> AuditRecords { get; } = [];
    public ObservableCollection<GuardAlert> Alerts { get; } = [];
    public ObservableCollection<AgentAdapterState> Agents { get; } = [];
    public ObservableCollection<CommandRule> CommandRules { get; } = [];
    public ObservableCollection<string> ProtectedDirectories { get; } = [];
    public ObservableCollection<string> ActiveAgentNames { get; } = [];
    public ObservableCollection<ProcessLifecycleEvent> ProcessEvents { get; } = [];
    public ObservableCollection<AgentHistoryRecord> HistoryRecords { get; } = [];
    public ObservableCollection<AgentSessionSummary> HistorySessions { get; } = [];
    public ObservableCollection<string> HistoryErrors { get; } = [];
    // v2.1.3 chart layout:
    public ObservableCollection<HourlyStatRow> HourlyStatsView { get; } = [];
    public ObservableCollection<TopAgentRow> TopAgentsView { get; } = [];
    // v2.1.1 multi-language dropdown:
    public ObservableCollection<LanguageChoice> AvailableLanguages { get; } = [];

    public MainViewModel(INotificationService? notificationService = null)
    {
        _paths.EnsureCreated();
        _sessionStore = new SessionStore(_store, _paths);
        _guardAnalyzer = new GuardAnalyzer(_store, _paths);
        _auditLog = new AuditLogService(_store, _paths, _guardAnalyzer);
        _hookInstaller = new HookInstaller(_paths, _store);
        _agentRegistry = new AgentRegistryService(_paths, _hookInstaller);
        _hookServer = new HookServer(_sessionStore, _auditLog, _guardAnalyzer, _store, _paths);
        _operationMonitor = new WindowsOperationMonitor(_auditLog, _guardAnalyzer, _paths);
        _historyScanner = new AgentSessionScanner(_paths);
        _notificationService = notificationService ?? new NullNotificationService();

        _sessionStore.Changed += (_, _) => Dispatch(SyncSessions);
        _guardAnalyzer.Changed += (_, _) => Dispatch(SyncGuard);
        _auditLog.Changed += (_, _) => Dispatch(SyncAudit);
        _hookServer.PendingRequestsChanged += (_, _) => Dispatch(SyncPendingRequests);
        _hookServer.PendingRequestsChanged += (_, _) => Dispatch(NotifyPendingIfNeeded);
        _hookServer.ServerMessage += (_, message) => Dispatch(() => StatusMessage = message);
        _operationMonitor.Changed += (_, _) => Dispatch(SyncMonitor);

        StartServerCommand = new RelayCommand(StartServerAsync, () => !IsServerRunning);
        StopServerCommand = new RelayCommand(StopServerAsync, () => IsServerRunning);
        StartMonitoringCommand = new RelayCommand(StartMonitoring, () => !IsMonitoring);
        StopMonitoringCommand = new RelayCommand(StopMonitoringAsync, () => IsMonitoring);
        RefreshAgentsCommand = new RelayCommand(RefreshAgentsAsync);
        InstallSelectedHooksCommand = new RelayCommand(InstallSelectedHooksAsync, () => SelectedAgent is { SupportsHookInstall: true });
        InstallAllHooksCommand = new RelayCommand(InstallAllHooksAsync);
        ApproveCommand = new RelayCommand(() => RespondPermissionAsync("allow"), () => SelectedPendingRequest is { Type: PendingRequestType.Permission });
        DenyCommand = new RelayCommand(() => RespondPermissionAsync("deny"), () => SelectedPendingRequest is { Type: PendingRequestType.Permission });
        AnswerQuestionCommand = new RelayCommand(AnswerQuestionAsync, () => SelectedPendingRequest is { Type: PendingRequestType.Question });
        AcceptPlanCommand = new RelayCommand(() => RespondPlanAsync("accept"), () => SelectedPendingRequest is { Type: PendingRequestType.Plan });
        CancelPlanCommand = new RelayCommand(() => RespondPlanAsync("cancel"), () => SelectedPendingRequest is { Type: PendingRequestType.Plan });
        AddProtectedDirectoryCommand = new RelayCommand(AddProtectedDirectory, () => !string.IsNullOrWhiteSpace(NewProtectedDirectory));
        RefreshAllCommand = new RelayCommand(RefreshAllAsync);
        ScanAgentHistoryCommand = new RelayCommand(ScanAgentHistoryAsync, () => !IsScanningHistory);
        ClearHistoryFilterCommand = new RelayCommand(ClearHistoryFilter);
        ExportAuditCommand = new RelayCommand(ExportAuditAsync);
        ChooseBridgePathCommand = new RelayCommand(ChooseBridgePath);
        ToggleNotificationsCommand = new RelayCommand(ToggleNotifications);

        // v2.1.1 multi-language dropdown
        foreach (var lang in LanguageChoice.BuildAll())
        {
            AvailableLanguages.Add(lang);
        }
    }

    public RelayCommand StartServerCommand { get; }
    public RelayCommand StopServerCommand { get; }
    public RelayCommand StartMonitoringCommand { get; }
    public RelayCommand StopMonitoringCommand { get; }
    public RelayCommand RefreshAgentsCommand { get; }
    public RelayCommand InstallSelectedHooksCommand { get; }
    public RelayCommand InstallAllHooksCommand { get; }
    public RelayCommand ApproveCommand { get; }
    public RelayCommand DenyCommand { get; }
    public RelayCommand AnswerQuestionCommand { get; }
    public RelayCommand AcceptPlanCommand { get; }
    public RelayCommand CancelPlanCommand { get; }
    public RelayCommand AddProtectedDirectoryCommand { get; }
    public RelayCommand RefreshAllCommand { get; }
    public RelayCommand ScanAgentHistoryCommand { get; }
    public RelayCommand ClearHistoryFilterCommand { get; }
    public RelayCommand ExportAuditCommand { get; }
    public RelayCommand ChooseBridgePathCommand { get; }
    public RelayCommand ToggleNotificationsCommand { get; }

    public PendingHookRequest? SelectedPendingRequest
    {
        get => _selectedPendingRequest;
        set
        {
            if (SetProperty(ref _selectedPendingRequest, value))
            {
                RaiseCommandStates();
                QuestionAnswer = "";
                ResponseReason = "";
            }
        }
    }

    public AgentAdapterState? SelectedAgent
    {
        get => _selectedAgent;
        set
        {
            if (SetProperty(ref _selectedAgent, value))
            {
                InstallSelectedHooksCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public AgentSessionSummary? SelectedSession
    {
        get => _selectedSession;
        set
        {
            if (SetProperty(ref _selectedSession, value))
            {
                if (value is null)
                {
                    ShowAllHistoryRecords();
                }
                else
                {
                    FilterHistoryToSession(value);
                }
            }
        }
    }

    public string HistoryStatusMessage
    {
        get => _historyStatusMessage;
        set => SetProperty(ref _historyStatusMessage, value);
    }

    public AgentSettings Settings
    {
        get => _settings;
        set
        {
            if (SetProperty(ref _settings, value))
            {
                _notificationService.SetEnabled(value.NotificationsEnabled);
            }
        }
    }

    public int HistoryDetailCount
    {
        get => _historyDetailCount;
        set => SetProperty(ref _historyDetailCount, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public string QuestionAnswer
    {
        get => _questionAnswer;
        set => SetProperty(ref _questionAnswer, value);
    }

    public string ResponseReason
    {
        get => _responseReason;
        set => SetProperty(ref _responseReason, value);
    }

    public string NewProtectedDirectory
    {
        get => _newProtectedDirectory;
        set
        {
            if (SetProperty(ref _newProtectedDirectory, value))
            {
                AddProtectedDirectoryCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsServerRunning => _hookServer.IsRunning;
    public bool IsMonitoring => _operationMonitor.IsMonitoring;
    public bool IsScanningHistory { get; private set; }
    public bool NotificationsEnabled
    {
        get => Settings.NotificationsEnabled;
        set
        {
            if (Settings.NotificationsEnabled == value)
            {
                return;
            }

            Settings.NotificationsEnabled = value;
            _notificationService.SetEnabled(value);
            OnPropertyChanged();
            _ = SaveSettingsAsync();
        }
    }

    public string BridgePath
    {
        get => Settings.BridgePath;
        set
        {
            if (Settings.BridgePath == value)
            {
                return;
            }

            Settings.BridgePath = value;
            OnPropertyChanged();
            _ = SaveSettingsAsync();
        }
    }
    public int PendingCount => PendingRequests.Count;
    public int ActiveSessionCount => Sessions.Count(item => item.IsActive || item.NeedsAttention);
    public int AuditCount => AuditRecords.Count;
    public int AlertCount => Alerts.Count;
    public int ActiveAgentCount => ActiveAgentNames.Count;
    public int InstalledAgentCount => Agents.Count(item => item.Status is AdapterStatus.Active or AdapterStatus.Installed);
    public int HistoryRecordCount => HistoryRecords.Count;
    public int HistorySessionCount => HistorySessions.Count;

    public async Task InitializeAsync()
    {
        var startupWarnings = new List<string>();
        await RunStartupStepAsync("load settings", LoadSettingsAsync, startupWarnings);
        _notificationService.SetEnabled(Settings.NotificationsEnabled);
        await RunStartupStepAsync("load guard data", _guardAnalyzer.LoadAsync, startupWarnings);
        await RunStartupStepAsync("load sessions", _sessionStore.LoadAsync, startupWarnings);
        await RunStartupStepAsync("load audit log", _auditLog.LoadAsync, startupWarnings);
        SyncGuard();
        SyncSessions();
        SyncAudit();
        SyncPendingRequests();
        await RunStartupStepAsync("refresh agents", RefreshAgentsAsync, startupWarnings);
        await RunStartupStepAsync("start hook server", StartServerAsync, startupWarnings);
        await RunStartupStepAsync("start Windows monitor", StartMonitoring, startupWarnings);
        StatusMessage = startupWarnings.Count == 0
            ? AppText.AgentGuardReady
            : AppText.AgentGuardReadyWithWarnings(string.Join("; ", startupWarnings));
    }

    public void ReportStartupError(Exception exception)
    {
        StatusMessage = AppText.StartupError(exception.Message);
    }

    public async ValueTask DisposeAsync()
    {
        await _operationMonitor.StopAsync();
        await _hookServer.StopAsync();
        await _guardAnalyzer.SaveAsync();
        await _sessionStore.SaveAsync();
        await _auditLog.SaveAsync();
        await SaveSettingsAsync();
    }

    private async Task StartServerAsync()
    {
        await _hookServer.StartAsync();
        SyncServerState();
    }

    private async Task StopServerAsync()
    {
        await _hookServer.StopAsync();
        SyncServerState();
    }

    private Task StartMonitoring()
    {
        _operationMonitor.Start(_guardAnalyzer.ProtectedDirectories);
        SyncMonitor();
        StatusMessage = AppText.WindowsMonitorRunning;
        RaiseRuntimeCommandStates();
        return Task.CompletedTask;
    }

    private async Task StopMonitoringAsync()
    {
        await _operationMonitor.StopAsync();
        SyncMonitor();
        StatusMessage = AppText.WindowsMonitorStopped;
        RaiseRuntimeCommandStates();
    }

    private async Task RefreshAgentsAsync()
    {
        var states = await _agentRegistry.RefreshAsync();
        Replace(Agents, states);
        OnPropertyChanged(nameof(InstalledAgentCount));
    }

    private async Task InstallSelectedHooksAsync()
    {
        if (SelectedAgent is null)
        {
            return;
        }

        StatusMessage = AppText.InstallingHooksFor(SelectedAgent.DisplayName);
        await _agentRegistry.InstallHooksAsync(SelectedAgent.AgentId, ResolveBridgePath());
        await RefreshAgentsAsync();
        StatusMessage = AppText.HooksUpdatedFor(SelectedAgent.DisplayName);
    }

    private async Task InstallAllHooksAsync()
    {
        StatusMessage = AppText.InstallingHooksForAvailableAgents;
        await _agentRegistry.InstallAllAvailableHooksAsync(ResolveBridgePath());
        await RefreshAgentsAsync();
        StatusMessage = AppText.AgentHookInstallationCompleted;
    }

    private async Task RespondPermissionAsync(string decision)
    {
        if (SelectedPendingRequest is null)
        {
            return;
        }

        await _hookServer.RespondAsync(SelectedPendingRequest.Id, new HookUserResponse
        {
            Decision = decision,
            Reason = string.IsNullOrWhiteSpace(ResponseReason) ? null : ResponseReason
        });
        StatusMessage = AppText.PermissionDecisionSent(decision, SelectedPendingRequest.Title);
    }

    private async Task AnswerQuestionAsync()
    {
        if (SelectedPendingRequest is null)
        {
            return;
        }

        await _hookServer.RespondAsync(SelectedPendingRequest.Id, new HookUserResponse
        {
            Answer = QuestionAnswer
        });
        StatusMessage = AppText.QuestionAnswerSent;
    }

    private async Task RespondPlanAsync(string mode)
    {
        if (SelectedPendingRequest is null)
        {
            return;
        }

        await _hookServer.RespondAsync(SelectedPendingRequest.Id, new HookUserResponse
        {
            Mode = mode,
            Message = string.IsNullOrWhiteSpace(ResponseReason) ? null : ResponseReason
        });
        StatusMessage = AppText.PlanModeSent(mode);
    }

    private Task AddProtectedDirectory()
    {
        if (!string.IsNullOrWhiteSpace(NewProtectedDirectory))
        {
            var added = _guardAnalyzer.AddProtectedDirectory(NewProtectedDirectory);
            NewProtectedDirectory = "";
            if (added && IsMonitoring)
            {
                _operationMonitor.UpdateWatchDirectories(_guardAnalyzer.ProtectedDirectories);
                SyncMonitor();
                StatusMessage = AppText.ProtectedDirectoryAddedAndMonitorRefreshed;
            }
            else if (added)
            {
                StatusMessage = AppText.ProtectedDirectoryAdded;
            }
            else
            {
                StatusMessage = AppText.ProtectedDirectoryAlreadyExists;
            }
        }

        return Task.CompletedTask;
    }

    private async Task RefreshAllAsync()
    {
        SyncAll();
        await RefreshAgentsAsync();
    }

    private async Task ScanAgentHistoryAsync()
    {
        if (IsScanningHistory) return;
        IsScanningHistory = true;
        ScanAgentHistoryCommand.RaiseCanExecuteChanged();
        HistoryStatusMessage = AppText.ScanningAgentHistory;
        try
        {
            var result = await _historyScanner.ScanAsync();
            _allHistoryRecords = result.Records;
            Replace(HistorySessions, result.Sessions);
            Replace(HistoryRecords, _allHistoryRecords);
            Replace(HistoryErrors, result.Errors);
            OnPropertyChanged(nameof(HistorySessionCount));
            OnPropertyChanged(nameof(HistoryRecordCount));
            HistoryDetailCount = _allHistoryRecords.Count;
            HistoryStatusMessage = AppText.AgentHistoryScanCompleted(result.Sessions.Count, result.Records.Count, result.ScannedFileCount);
            _ = _store.WriteAsync(_paths.HistoryCachePath, result);
        }
        catch (Exception ex)
        {
            HistoryStatusMessage = AppText.AgentHistoryScanFailed(ex.Message);
        }
        finally
        {
            IsScanningHistory = false;
            ScanAgentHistoryCommand.RaiseCanExecuteChanged();
        }
    }

    private async Task ExportAuditAsync()
    {
        try
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "CSV (*.csv)|*.csv|JSON (*.json)|*.json",
                FileName = $"agentguard-audit-{DateTime.Now:yyyyMMdd-HHmmss}.csv",
                Title = AppText.ExportAuditDialogTitle
            };
            if (dialog.ShowDialog() != true) return;
            var records = _auditLog.Snapshot(5000);
            await WriteAuditExportAsync(dialog.FileName, records);
            StatusMessage = AppText.AuditExported(dialog.FileName, records.Count);
        }
        catch (Exception ex)
        {
            StatusMessage = AppText.AuditExportFailed(ex.Message);
        }
    }

    private void ChooseBridgePath()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "agentguard-bridge (*.exe)|*.exe|All files (*.*)|*.*",
            Title = AppText.ChooseBridgePathTitle
        };
        if (dialog.ShowDialog() == true)
        {
            BridgePath = dialog.FileName;
        }
    }

    private void ToggleNotifications()
    {
        Settings.NotificationsEnabled = !Settings.NotificationsEnabled;
        _notificationService.SetEnabled(Settings.NotificationsEnabled);
        OnPropertyChanged(nameof(Settings));
        OnPropertyChanged(nameof(NotificationsEnabled));
        _ = SaveSettingsAsync();
    }

    private void NotifyPendingIfNeeded()
    {
        if (!Settings.NotificationsEnabled) return;
        var pending = _hookServer.PendingRequests;
        if (pending.Count == 0) return;
        var top = pending[0];
        _notificationService.Show(new NotificationMessage
        {
            Kind = NotificationKind.PendingApproval,
            Title = AppText.NotificationPendingTitle(top.AgentName),
            Body = $"{top.Title}\n{top.Detail}"
        });
    }

    private async Task LoadSettingsAsync()
    {
        var loaded = await _store.ReadAsync<AgentSettings>(_paths.SettingsPath) ?? new AgentSettings();
        Settings = loaded;
        AppText.ActiveLanguage = loaded.Language;
        _selectedLanguage = AvailableLanguages.FirstOrDefault(l => l.Code == loaded.Language)
            ?? AvailableLanguages.FirstOrDefault();
        OnPropertyChanged(nameof(SelectedLanguage));
        OnPropertyChanged(nameof(NotificationsEnabled));
        OnPropertyChanged(nameof(BridgePath));
    }

    private Task SaveSettingsAsync() => _store.WriteAsync(_paths.SettingsPath, Settings);

    private void FilterHistoryToSession(AgentSessionSummary session)
    {
        var filtered = _allHistoryRecords
            .Where(item => item.SessionId == session.Id || item.SourceFile == session.SourceFile)
            .ToList();
        Replace(HistoryRecords, filtered);
        OnPropertyChanged(nameof(HistoryRecordCount));
        HistoryDetailCount = filtered.Count;
    }

    private void ClearHistoryFilter()
    {
        SelectedSession = null;
        ShowAllHistoryRecords();
    }

    private void ShowAllHistoryRecords()
    {
        Replace(HistoryRecords, _allHistoryRecords);
        OnPropertyChanged(nameof(HistoryRecordCount));
        HistoryDetailCount = _allHistoryRecords.Count;
    }

    private string? ResolveBridgePath()
    {
        if (!string.IsNullOrWhiteSpace(Settings.BridgePath) && File.Exists(Settings.BridgePath))
        {
            return Settings.BridgePath;
        }
        var path = Path.Combine(AppContext.BaseDirectory, "agentguard-bridge.exe");
        return File.Exists(path) ? path : null;
    }

    private static async Task WriteAuditExportAsync(string path, IReadOnlyList<OperationRecord> records)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext == ".json")
        {
            await File.WriteAllTextAsync(path,
                System.Text.Json.JsonSerializer.Serialize(records, JsonFileStore.Options),
                System.Text.Encoding.UTF8);
        }
        else
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Timestamp,Agent,Operation,Path,Detail,FileSize,ProcessName");
            foreach (var r in records)
            {
                sb.Append(Csv(r.Timestamp.ToString("O"))).Append(',');
                sb.Append(Csv(r.AgentName)).Append(',');
                sb.Append(Csv(r.OperationType.ToString())).Append(',');
                sb.Append(Csv(r.TargetPath)).Append(',');
                sb.Append(Csv(r.Detail)).Append(',');
                sb.Append(r.FileSize).Append(',');
                sb.AppendLine(Csv(r.ProcessName));
            }
            await File.WriteAllTextAsync(path, sb.ToString(), System.Text.Encoding.UTF8);
        }
    }

    private static string Csv(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var needsQuote = value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r');
        if (!needsQuote) return value;
        return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    private void SyncAll()
    {
        SyncSessions();
        SyncPendingRequests();
        SyncAudit();
        SyncGuard();
        SyncMonitor();
    }

    private void SyncSessions()
    {
        Replace(Sessions, _sessionStore.Snapshot());
        OnPropertyChanged(nameof(ActiveSessionCount));
    }

    private void SyncPendingRequests()
    {
        Replace(PendingRequests, _hookServer.PendingRequests);
        if (SelectedPendingRequest is not null &&
            PendingRequests.All(item => item.Id != SelectedPendingRequest.Id))
        {
            SelectedPendingRequest = PendingRequests.FirstOrDefault();
        }

        OnPropertyChanged(nameof(PendingCount));
        RaiseCommandStates();
    }

    private void SyncServerState()
    {
        OnPropertyChanged(nameof(IsServerRunning));
        RaiseRuntimeCommandStates();
    }

    private void SyncAudit()
    {
        Replace(AuditRecords, _auditLog.Snapshot(500));
        OnPropertyChanged(nameof(AuditCount));
        RefreshHourlyStatsView();
        RefreshTopAgentsView();
    }

    private void SyncGuard()
    {
        Replace(Alerts, _guardAnalyzer.Alerts.Take(500));
        Replace(CommandRules, _guardAnalyzer.CommandRules);
        Replace(ProtectedDirectories, _guardAnalyzer.ProtectedDirectories);
        OnPropertyChanged(nameof(AlertCount));
    }

    private void SyncMonitor()
    {
        Replace(ActiveAgentNames, _operationMonitor.ActiveAgentNames.OrderBy(item => item));
        Replace(ProcessEvents, _operationMonitor.ProcessEvents.Take(500));
        OnPropertyChanged(nameof(IsMonitoring));
        OnPropertyChanged(nameof(ActiveAgentCount));
        RaiseRuntimeCommandStates();
    }

    private void RaiseCommandStates()
    {
        ApproveCommand.RaiseCanExecuteChanged();
        DenyCommand.RaiseCanExecuteChanged();
        AnswerQuestionCommand.RaiseCanExecuteChanged();
        AcceptPlanCommand.RaiseCanExecuteChanged();
        CancelPlanCommand.RaiseCanExecuteChanged();
    }

    private void RaiseRuntimeCommandStates()
    {
        StartServerCommand.RaiseCanExecuteChanged();
        StopServerCommand.RaiseCanExecuteChanged();
        StartMonitoringCommand.RaiseCanExecuteChanged();
        StopMonitoringCommand.RaiseCanExecuteChanged();
    }

    private static void Replace<T>(ObservableCollection<T> collection, IEnumerable<T> values)
    {
        collection.Clear();
        foreach (var value in values)
        {
            collection.Add(value);
        }
    }

    // === v2.1.1 multi-language dropdown ===
    private LanguageChoice? _selectedLanguage;
    public LanguageChoice? SelectedLanguage
    {
        get => _selectedLanguage;
        set
        {
            if (value is null || _selectedLanguage == value) return;
            _selectedLanguage = value;
            OnPropertyChanged();
            Settings.Language = value.Code;
            AppText.ActiveLanguage = value.Code;
            _ = SaveSettingsAsync();
            // Refresh all on-screen strings bound to x:Static AppText.* by
            // toggling a refresh marker; for simplicity we just trigger
            // SyncMonitor/SyncAudit/SyncGuard, which the XAML localizes
            // via x:Static references that re-evaluate on demand.
            SyncMonitor();
            SyncAudit();
            SyncGuard();
        }
    }

    // === v2.1.3 chart layout ===
    private void RefreshHourlyStatsView()
    {
        var stats = _guardAnalyzer.RecentHourlyStats(24);
        HourlyStatsView.Clear();
        var max = Math.Max(1, stats.Max(s => s.CreateCount + s.ModifyCount + s.DeleteCount));
        foreach (var s in stats)
        {
            var total = s.CreateCount + s.ModifyCount + s.DeleteCount;
            var barWidth = (int)Math.Round(total * 24.0 / max);
            HourlyStatsView.Add(new HourlyStatRow
            {
                Label = s.Hour.ToString("HH:mm"),
                Bar = new string('█', Math.Max(0, barWidth)),
                CountText = total.ToString(),
            });
        }
    }

    private void RefreshTopAgentsView()
    {
        var top = _guardAnalyzer.TopAgents(_auditLog.Snapshot(2000), top: 5);
        TopAgentsView.Clear();
        if (top.Count == 0)
        {
            TopAgentsView.Add(new TopAgentRow { Agent = AppText.TopAgentsEmpty, Bar = "", CountText = "" });
            return;
        }
        var max = Math.Max(1, top.Max(x => x.Count));
        foreach (var (agent, count) in top)
        {
            var barWidth = (int)Math.Round(count * 24.0 / max);
            TopAgentsView.Add(new TopAgentRow
            {
                Agent = agent,
                Bar = new string('█', Math.Max(0, barWidth)),
                CountText = count.ToString(),
            });
        }
    }

    private static void Dispatch(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            dispatcher.BeginInvoke(action);
        }
    }

    private static async Task RunStartupStepAsync(string name, Func<CancellationToken, Task> action, List<string> warnings)
    {
        try
        {
            await action(CancellationToken.None);
        }
        catch (Exception ex)
        {
            AppDiagnostics.Log($"Startup step failed: {name}.", ex);
            warnings.Add($"{name}: {ex.Message}");
        }
    }

    private static async Task RunStartupStepAsync(string name, Func<Task> action, List<string> warnings)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            AppDiagnostics.Log($"Startup step failed: {name}.", ex);
            warnings.Add($"{name}: {ex.Message}");
        }
    }
}

public sealed class HourlyStatRow
{
    public string Label { get; set; } = "";
    public string Bar { get; set; } = "";
    public string CountText { get; set; } = "";
}

public sealed class TopAgentRow
{
    public string Agent { get; set; } = "";
    public string Bar { get; set; } = "";
    public string CountText { get; set; } = "";
}

public sealed class LanguageChoice
{
    public string Code { get; init; } = "";
    public string Display { get; init; } = "";

    public static IReadOnlyList<LanguageChoice> BuildAll() =>
        AppText.AvailableLanguageCodes
            .Select(code => new LanguageChoice { Code = code, Display = AppText.LanguageDisplay(code) })
            .ToList();
}
