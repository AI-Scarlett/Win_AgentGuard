namespace AgentGuard.Core.Services;

public enum NotificationKind
{
    PendingApproval,
    CriticalAlert,
    ProcessLaunch,
    AuditExport
}

public sealed class NotificationMessage
{
    public NotificationKind Kind { get; init; }
    public string Title { get; init; } = "";
    public string Body { get; init; } = "";
    public string? ActionUri { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.Now;
}

/// <summary>
/// Cross-platform notification abstraction. The Core library ships a no-op
/// default; the WPF host wires up a Windows toast implementation.
/// </summary>
public interface INotificationService
{
    bool IsEnabled { get; }
    void Show(NotificationMessage message);
    void SetEnabled(bool enabled);
}

public sealed class NullNotificationService : INotificationService
{
    public bool IsEnabled => false;
    public void Show(NotificationMessage message) { }
    public void SetEnabled(bool enabled) { }
}
