using System.Text.RegularExpressions;
using AgentGuard.Core.Localization;
using AgentGuard.Core.Models;

namespace AgentGuard.Core.Services;

public sealed class GuardAnalyzer
{
    private readonly JsonFileStore _store;
    private readonly AppPaths _paths;
    private readonly List<DateTimeOffset> _recentDeletes = [];
    private readonly List<DateTimeOffset> _recentModifies = [];
    private readonly Dictionary<string, DateTimeOffset> _lastAlertTimes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Regex> _regexCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _statsGate = new();
    private DateTimeOffset _lastStatsSave = DateTimeOffset.MinValue;

    public AlertRule AlertRule { get; private set; } = new();
    public List<GuardAlert> Alerts { get; private set; } = [];
    public List<CommandRule> CommandRules { get; private set; } = CommandRule.DefaultRules();
    public List<string> ProtectedDirectories { get; private set; } = [];
    public List<HourlyStats> HourlyStats { get; private set; } = [];

    public event EventHandler? Changed;

    public GuardAnalyzer(JsonFileStore store, AppPaths paths)
    {
        _store = store;
        _paths = paths;
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        AlertRule = await _store.ReadAsync<AlertRule>(_paths.AlertRulePath, cancellationToken) ?? new AlertRule();
        Alerts = await _store.ReadAsync<List<GuardAlert>>(_paths.AlertsPath, cancellationToken) ?? [];
        CommandRules = await _store.ReadAsync<List<CommandRule>>(_paths.CommandRulesPath, cancellationToken) ?? CommandRule.DefaultRules();
        if (CommandRules.Count == 0)
        {
            CommandRules = CommandRule.DefaultRules();
        }

        ProtectedDirectories = await _store.ReadAsync<List<string>>(_paths.ProtectedDirectoriesPath, cancellationToken) ?? DefaultProtectedDirectories();
        HourlyStats = await _store.ReadAsync<List<HourlyStats>>(_paths.HourlyStatsPath, cancellationToken) ?? [];
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        await _store.WriteAsync(_paths.AlertRulePath, AlertRule, cancellationToken);
        await _store.WriteAsync(_paths.AlertsPath, Alerts.Take(500).ToList(), cancellationToken);
        await _store.WriteAsync(_paths.CommandRulesPath, CommandRules, cancellationToken);
        await _store.WriteAsync(_paths.ProtectedDirectoriesPath, ProtectedDirectories, cancellationToken);
        await _store.WriteAsync(_paths.HourlyStatsPath, HourlyStats, cancellationToken);
    }

    public void Analyze(OperationRecord record)
    {
        RecordStats(record);
        CheckBatchOperation(record);
        CheckProtectedDirectory(record);
        CheckSensitiveFile(record);

        if (record.OperationType == OperationType.Execute)
        {
            RecordObservedCommand(string.IsNullOrWhiteSpace(record.Detail) ? record.TargetPath : record.Detail, record.AgentName);
        }
    }

    public void CheckProcessLifecycle(ProcessLifecycleEvent lifecycleEvent)
    {
        if (!AlertRule.ProcessLaunchAlertEnabled)
        {
            return;
        }

        if (lifecycleEvent.EventType.Equals("launch", StringComparison.OrdinalIgnoreCase))
        {
            FireAlert(new GuardAlert
            {
                AlertType = AlertType.ProcessLaunch,
                Severity = AlertSeverity.Info,
                Title = CoreText.AgentProcessLaunchedTitle,
                Message = CoreText.AgentProcessLaunchedMessage(lifecycleEvent.AgentName, lifecycleEvent.ProcessId),
                AgentName = lifecycleEvent.AgentName,
                TargetPath = lifecycleEvent.ProcessName,
                Detail = lifecycleEvent.Arguments
            }, $"process_launch_{lifecycleEvent.AgentName}");
        }
    }

    public CommandCheckResult RecordObservedCommand(string command, string agentName)
    {
        var trimmed = command.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return new CommandCheckResult { Message = CoreText.EmptyCommand };
        }

        var blacklisted = MatchRule(trimmed, CommandListType.Blacklist);
        if (blacklisted is not null)
        {
            IncrementRule(blacklisted, agentName);
            FireAlert(new GuardAlert
            {
                AlertType = AlertType.CommandBlocked,
                Severity = AlertSeverity.Critical,
                Title = CoreText.BlockedCommandPatternTitle,
                Message = CoreText.BlockedCommandMessage(agentName, blacklisted.Pattern),
                AgentName = agentName,
                TargetPath = trimmed,
                Detail = blacklisted.Consequence
            }, $"command_blocked_{blacklisted.Id}");
            _ = SaveAsync();
            return new CommandCheckResult
            {
                IsBlocked = AlertRule.CommandGuardEnabled,
                Rule = blacklisted,
                Message = blacklisted.Consequence
            };
        }

        var whitelisted = MatchRule(trimmed, CommandListType.Whitelist);
        if (whitelisted is not null)
        {
            IncrementRule(whitelisted, agentName);
            _ = SaveAsync();
            return new CommandCheckResult { IsAllowed = true, Rule = whitelisted, Message = whitelisted.Description };
        }

        var unclassified = MatchRule(trimmed, CommandListType.Unclassified);
        if (unclassified is not null)
        {
            IncrementRule(unclassified, agentName);
            _ = SaveAsync();
            return new CommandCheckResult { Rule = unclassified, Message = unclassified.Description };
        }

        var normalized = NormalizeCommandPattern(trimmed);
        var rule = new CommandRule
        {
            Pattern = normalized,
            ListType = CommandListType.Unclassified,
            Description = CoreText.DiscoveredCommandDescription,
            Source = "discovered"
        };
        CommandRules.Insert(0, rule);
        IncrementRule(rule, agentName);
        Changed?.Invoke(this, EventArgs.Empty);
        _ = SaveAsync();
        return new CommandCheckResult { Rule = rule, Message = CoreText.DiscoveredCommandMessage };
    }

    public bool AddProtectedDirectory(string path)
    {
        var expanded = _paths.ExpandUserPath(path);
        if (!ProtectedDirectories.Contains(expanded, StringComparer.OrdinalIgnoreCase))
        {
            ProtectedDirectories.Add(expanded);
            Changed?.Invoke(this, EventArgs.Empty);
            _ = SaveAsync();
            return true;
        }

        return false;
    }

    public AuditReport GenerateAuditReport(IReadOnlyList<OperationRecord> records, DateTimeOffset start, DateTimeOffset end)
    {
        var scoped = records.Where(item => item.Timestamp >= start && item.Timestamp <= end).ToList();
        return new AuditReport
        {
            StartTime = start,
            EndTime = end,
            TotalOperations = scoped.Count,
            AgentBreakdown = scoped.GroupBy(item => item.AgentName).ToDictionary(g => g.Key, g => g.Count()),
            OperationTypeBreakdown = scoped.GroupBy(item => item.OperationType.ToString()).ToDictionary(g => g.Key, g => g.Count()),
            AlertCount = Alerts.Count(item => item.Timestamp >= start && item.Timestamp <= end),
            CriticalAlertCount = Alerts.Count(item => item.Timestamp >= start && item.Timestamp <= end && item.Severity == AlertSeverity.Critical),
            TopTargetPaths = scoped.GroupBy(item => item.TargetPath)
                .OrderByDescending(g => g.Count())
                .Take(10)
                .Select(g => g.Key)
                .ToList(),
            Summary = CoreText.AuditReportSummary(scoped.Count, scoped.Select(item => item.AgentName).Distinct().Count())
        };
    }

    private void CheckBatchOperation(OperationRecord record)
    {
        var now = DateTimeOffset.Now;
        var window = TimeSpan.FromSeconds(AlertRule.BatchTimeWindowSeconds);
        if (record.OperationType == OperationType.Delete)
        {
            _recentDeletes.Add(now);
            _recentDeletes.RemoveAll(item => now - item > window);
            if (_recentDeletes.Count >= AlertRule.BatchDeleteThreshold)
            {
                FireAlert(new GuardAlert
                {
                    AlertType = AlertType.BatchDelete,
                    Severity = AlertSeverity.Critical,
                    Title = CoreText.BatchDeleteAlertTitle,
                    Message = CoreText.BatchDeleteAlertMessage(record.AgentName, _recentDeletes.Count, window.TotalSeconds),
                    AgentName = record.AgentName,
                    TargetPath = record.TargetPath,
                    Detail = CoreText.DeleteEventsDetail(_recentDeletes.Count)
                }, $"batch_delete_{record.AgentName}");
                _recentDeletes.Clear();
            }
        }

        if (record.OperationType == OperationType.Modify)
        {
            _recentModifies.Add(now);
            _recentModifies.RemoveAll(item => now - item > window);
            if (_recentModifies.Count >= AlertRule.BatchModifyThreshold)
            {
                FireAlert(new GuardAlert
                {
                    AlertType = AlertType.BatchModify,
                    Severity = AlertSeverity.Warning,
                    Title = CoreText.BatchModifyAlertTitle,
                    Message = CoreText.BatchModifyAlertMessage(record.AgentName, _recentModifies.Count, window.TotalSeconds),
                    AgentName = record.AgentName,
                    TargetPath = record.TargetPath,
                    Detail = CoreText.ModifyEventsDetail(_recentModifies.Count)
                }, $"batch_modify_{record.AgentName}");
                _recentModifies.Clear();
            }
        }
    }

    private void CheckProtectedDirectory(OperationRecord record)
    {
        if (!AlertRule.ProtectedDirectoryAlertEnabled ||
            record.OperationType is not (OperationType.Delete or OperationType.Modify or OperationType.Create))
        {
            return;
        }

        var target = _paths.ExpandUserPath(record.TargetPath);
        foreach (var protectedDirectory in ProtectedDirectories)
        {
            if (target.StartsWith(_paths.ExpandUserPath(protectedDirectory), StringComparison.OrdinalIgnoreCase))
            {
                FireAlert(new GuardAlert
                {
                    AlertType = AlertType.ProtectedDirectory,
                    Severity = AlertSeverity.Warning,
                    Title = CoreText.ProtectedDirectoryAccessTitle,
                    Message = CoreText.ProtectedDirectoryAccessMessage(record.AgentName, record.OperationType),
                    AgentName = record.AgentName,
                    TargetPath = record.TargetPath,
                    Detail = protectedDirectory
                }, $"protected_{record.TargetPath}");
                return;
            }
        }
    }

    private void CheckSensitiveFile(OperationRecord record)
    {
        if (!AlertRule.SensitiveFileDetectionEnabled)
        {
            return;
        }

        var fileName = Path.GetFileName(record.TargetPath).ToLowerInvariant();
        var lowerPath = record.TargetPath.ToLowerInvariant();
        var matched = SensitiveFileRules.FirstOrDefault(rule =>
            rule.NamePatterns.Any(pattern => MatchesFilePattern(fileName, pattern)) ||
            rule.PathPatterns.Any(lowerPath.Contains));

        if (matched is not null)
        {
            FireAlert(new GuardAlert
            {
                AlertType = AlertType.SensitiveFile,
                Severity = AlertSeverity.Critical,
                Title = CoreText.SensitiveFileAccessTitle,
                Message = CoreText.SensitiveFileAccessMessage(record.AgentName, fileName),
                AgentName = record.AgentName,
                TargetPath = record.TargetPath,
                Detail = matched.Description
            }, $"sensitive_file_{record.TargetPath}");
            return;
        }

        if (AlertRule.SensitiveContentDetectionEnabled)
        {
            CheckSensitiveContent(record);
        }
    }

    private void CheckSensitiveContent(OperationRecord record)
    {
        if (record.OperationType is not (OperationType.Create or OperationType.Modify))
        {
            return;
        }

        var path = _paths.ExpandUserPath(record.TargetPath);
        if (!File.Exists(path) || !TextExtensions.Contains(Path.GetExtension(path).TrimStart('.').ToLowerInvariant()))
        {
            return;
        }

        try
        {
            var content = File.ReadAllText(path);
            var limited = content.Length > 50000 ? content[..50000] : content;
            foreach (var (pattern, description) in SensitiveContentPatterns)
            {
                if (limited.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                {
                    FireAlert(new GuardAlert
                    {
                        AlertType = AlertType.SensitiveContent,
                        Severity = AlertSeverity.Critical,
                        Title = CoreText.SensitiveContentDetectedTitle,
                        Message = CoreText.SensitiveContentDetectedMessage(record.AgentName, description),
                        AgentName = record.AgentName,
                        TargetPath = record.TargetPath,
                        Detail = description
                    }, $"sensitive_content_{record.TargetPath}");
                    return;
                }
            }
        }
        catch
        {
            // Files may be locked or deleted before inspection. Keep monitoring alive.
        }
    }

    private void RecordStats(OperationRecord record)
    {
        List<HourlyStats>? statsSnapshot = null;
        lock (_statsGate)
        {
            var hour = new DateTimeOffset(record.Timestamp.Year, record.Timestamp.Month, record.Timestamp.Day, record.Timestamp.Hour, 0, 0, record.Timestamp.Offset);
            var id = hour.ToString("O");
            var stats = HourlyStats.FirstOrDefault(item => item.Id == id);
            if (stats is null)
            {
                stats = new HourlyStats { Id = id, Hour = hour };
                HourlyStats.Add(stats);
            }

            switch (record.OperationType)
            {
                case OperationType.Create: stats.CreateCount++; break;
                case OperationType.Modify: stats.ModifyCount++; break;
                case OperationType.Delete: stats.DeleteCount++; break;
                case OperationType.Read: stats.ReadCount++; break;
                case OperationType.Move: stats.MoveCount++; break;
                case OperationType.Rename: stats.RenameCount++; break;
                case OperationType.Execute: stats.ExecuteCount++; break;
            }

            if (HourlyStats.Count > 168)
            {
                HourlyStats = HourlyStats.OrderBy(item => item.Hour).TakeLast(168).ToList();
            }

            // Atomic check-and-set inside the gate to avoid duplicate IO.
            if (DateTimeOffset.Now - _lastStatsSave > TimeSpan.FromSeconds(30))
            {
                _lastStatsSave = DateTimeOffset.Now;
                statsSnapshot = HourlyStats.Select(item => new HourlyStats
                {
                    Id = item.Id,
                    Hour = item.Hour,
                    CreateCount = item.CreateCount,
                    ModifyCount = item.ModifyCount,
                    DeleteCount = item.DeleteCount,
                    ReadCount = item.ReadCount,
                    MoveCount = item.MoveCount,
                    RenameCount = item.RenameCount,
                    ExecuteCount = item.ExecuteCount
                }).ToList();
            }
        }

        if (statsSnapshot is not null)
        {
            _ = _store.WriteAsync(_paths.HourlyStatsPath, statsSnapshot);
        }
    }

    private CommandRule? MatchRule(string command, CommandListType type)
    {
        return CommandRules.Where(item => item.ListType == type)
            .FirstOrDefault(item => MatchesCommand(command, item));
    }

    private bool MatchesCommand(string command, CommandRule rule)
    {
        if (rule.IsRegex)
        {
            try
            {
                if (!_regexCache.TryGetValue(rule.Id, out var regex))
                {
                    regex = new Regex(rule.Pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
                    _regexCache[rule.Id] = regex;
                }

                return regex.IsMatch(command);
            }
            catch
            {
                return false;
            }
        }

        return command.Contains(rule.Pattern, StringComparison.OrdinalIgnoreCase);
    }

    private static void IncrementRule(CommandRule rule, string agentName)
    {
        var now = DateTimeOffset.Now;
        var today = now.ToString("yyyy-MM-dd");
        var lastDate = rule.LastCalledBy.Split(new[] { '|' }, StringSplitOptions.None).FirstOrDefault() ?? "";
        rule.TotalCallCount++;
        rule.TodayCallCount = lastDate == today ? rule.TodayCallCount + 1 : 1;
        rule.LastCalledBy = $"{today}|{agentName}";
    }

    private void FireAlert(GuardAlert alert, string key)
    {
        var now = DateTimeOffset.Now;
        if (_lastAlertTimes.TryGetValue(key, out var last) &&
            now - last < TimeSpan.FromSeconds(AlertRule.AlertCooldownSeconds))
        {
            return;
        }

        _lastAlertTimes[key] = now;
        Alerts.Insert(0, alert);
        if (Alerts.Count > 500)
        {
            Alerts.RemoveRange(500, Alerts.Count - 500);
        }

        Changed?.Invoke(this, EventArgs.Empty);
        _ = SaveAsync();
    }

    private static string NormalizeCommandPattern(string command)
    {
        var first = command.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return string.IsNullOrWhiteSpace(first) ? command : first;
    }

    private List<string> DefaultProtectedDirectories()
    {
        var defaults = new List<string>();
        foreach (var folder in new[]
                 {
                     Environment.SpecialFolder.DesktopDirectory,
                     Environment.SpecialFolder.MyDocuments
                 })
        {
            var path = Environment.GetFolderPath(folder);
            if (!string.IsNullOrWhiteSpace(path))
            {
                defaults.Add(path);
            }
        }

        var downloads = Path.Combine(_paths.UserProfile, "Downloads");
        if (Directory.Exists(downloads))
        {
            defaults.Add(downloads);
        }

        return defaults.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static bool MatchesFilePattern(string fileName, string pattern)
    {
        pattern = pattern.ToLowerInvariant();
        if (pattern.Contains('*', StringComparison.Ordinal))
        {
            var regex = "^" + Regex.Escape(pattern).Replace("\\*", ".*", StringComparison.Ordinal) + "$";
            return Regex.IsMatch(fileName, regex, RegexOptions.IgnoreCase);
        }

        return fileName.Equals(pattern, StringComparison.OrdinalIgnoreCase) ||
               fileName.StartsWith(pattern, StringComparison.OrdinalIgnoreCase);
    }

    private sealed record SensitiveRule(string[] NamePatterns, string[] PathPatterns, string Description);

    private static readonly SensitiveRule[] SensitiveFileRules =
    [
        new([".env", ".env.local", ".env.production", ".env.staging"], [], CoreText.EnvironmentVariableFile),
        new(["id_rsa", "id_ed25519", "id_ecdsa", "*.pem", "*.key", "*.p12", "*.pfx", "*.jks"], [".ssh"], CoreText.PrivateKeyOrCertificate),
        new(["credentials", "credentials.json", "service-account*.json", "*.credentials"], [], CoreText.CloudServiceCredentials),
        new([".npmrc", ".pypirc", "credentials"], [".gem"], CoreText.PackageManagerAuthToken),
        new(["*.keystore", "keystore.jks", "google-services.json"], [], CoreText.MobileAppSecret)
    ];

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "", "txt", "md", "json", "yaml", "yml", "toml", "ini", "cfg", "conf", "env",
        "ps1", "bat", "cmd", "sh", "py", "js", "ts", "rb", "go", "rs", "java",
        "xml", "html", "css", "sql", "log", "cs", "csproj"
    };

    private static readonly (string Pattern, string Description)[] SensitiveContentPatterns =
    [
        ("sk-", "OpenAI API key"),
        ("pk_", "Stripe API key"),
        ("AKIA", "AWS access key"),
        ("AIza", "Google API key"),
        ("ghp_", "GitHub token"),
        ("github_pat_", "GitHub personal access token"),
        ("xoxb-", "Slack bot token"),
        ("hooks.slack.com/services/T", "Slack webhook"),
        ("-----BEGIN RSA PRIVATE KEY-----", "RSA private key"),
        ("-----BEGIN PRIVATE KEY-----", "Private key"),
        ("\"type\": \"service_account\"", "GCP service account"),
        ("aws_secret_access_key", "AWS secret key"),
        ("api_key", "API key reference"),
        ("secret_key", "Secret key reference"),
        ("private_key", "Private key reference"),
        ("password", "Password reference")
    ];
}
