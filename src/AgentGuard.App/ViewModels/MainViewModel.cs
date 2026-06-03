using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
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

    private PendingHookRequest? _selectedPendingRequest;
    private AgentAdapterState? _selectedAgent;
    private string _statusMessage = AppText.StartingAgentGuard;
    private string _questionAnswer = "";
    private string _responseReason = "";
    private string _newProtectedDirectory = "";

    public ObservableCollection<SessionState> Sessions { get; } = [];
    public ObservableCollection<PendingHookRequest> PendingRequests { get; } = [];
    public ObservableCollection<OperationRecord> AuditRecords { get; } = [];
    public ObservableCollection<GuardAlert> Alerts { get; } = [];
    public ObservableCollection<AgentAdapterState> Agents { get; } = [];
    public ObservableCollection<CommandRule> CommandRules { get; } = [];
    public ObservableCollection<string> ProtectedDirectories { get; } = [];
    public ObservableCollection<string> ActiveAgentNames { get; } = [];
    public ObservableCollection<ProcessLifecycleEvent> ProcessEvents { get; } = [];

    public MainViewModel()
    {
        _paths.EnsureCreated();
        _sessionStore = new SessionStore(_store, _paths);
        _guardAnalyzer = new GuardAnalyzer(_store, _paths);
        _auditLog = new AuditLogService(_store, _paths, _guardAnalyzer);
        _hookInstaller = new HookInstaller(_paths, _store);
        _agentRegistry = new AgentRegistryService(_paths, _hookInstaller);
        _hookServer = new HookServer(_sessionStore, _auditLog, _guardAnalyzer, _store, _paths);
        _operationMonitor = new WindowsOperationMonitor(_auditLog, _guardAnalyzer, _paths);

        _sessionStore.Changed += (_, _) => Dispatch(SyncSessions);
        _guardAnalyzer.Changed += (_, _) => Dispatch(SyncGuard);
        _auditLog.Changed += (_, _) => Dispatch(SyncAudit);
        _hookServer.PendingRequestsChanged += (_, _) => Dispatch(SyncPendingRequests);
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
    public int PendingCount => PendingRequests.Count;
    public int ActiveSessionCount => Sessions.Count(item => item.IsActive || item.NeedsAttention);
    public int AuditCount => AuditRecords.Count;
    public int AlertCount => Alerts.Count;
    public int ActiveAgentCount => ActiveAgentNames.Count;
    public int InstalledAgentCount => Agents.Count(item => item.Status is AdapterStatus.Active or AdapterStatus.Installed);

    public async Task InitializeAsync()
    {
        await _guardAnalyzer.LoadAsync();
        await _sessionStore.LoadAsync();
        await _auditLog.LoadAsync();
        SyncGuard();
        SyncSessions();
        SyncAudit();
        SyncPendingRequests();
        await RefreshAgentsAsync();
        await StartServerAsync();
        await StartMonitoring();
        StatusMessage = AppText.AgentGuardReady;
    }

    public async ValueTask DisposeAsync()
    {
        await _operationMonitor.StopAsync();
        await _hookServer.StopAsync();
        await _guardAnalyzer.SaveAsync();
        await _sessionStore.SaveAsync();
        await _auditLog.SaveAsync();
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
        await _agentRegistry.InstallHooksAsync(SelectedAgent.AgentId, BridgeExecutablePath());
        await RefreshAgentsAsync();
        StatusMessage = AppText.HooksUpdatedFor(SelectedAgent.DisplayName);
    }

    private async Task InstallAllHooksAsync()
    {
        StatusMessage = AppText.InstallingHooksForAvailableAgents;
        await _agentRegistry.InstallAllAvailableHooksAsync(BridgeExecutablePath());
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

    private static string? BridgeExecutablePath()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "agentguard-bridge.exe");
        return File.Exists(path) ? path : null;
    }
}
