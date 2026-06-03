using AgentGuard.Core.Localization;
using AgentGuard.Core.Models;

namespace AgentGuard.Core.Services;

public static class HookAuditMapper
{
    private static readonly HashSet<string> RecordableEvents = new(StringComparer.OrdinalIgnoreCase)
    {
        "PreToolUse",
        "pre_tool_use",
        "PostToolUse",
        "post_tool_use",
        "PostToolUseFailure",
        "post_tool_use_failure",
        "PermissionDenied",
        "permission_denied",
        "PermissionRequest",
        "permission_request",
        "ShellExecutionStart",
        "shell_execution_start",
        "ShellExecutionEnd",
        "shell_execution_end",
        "MCPExecutionStart",
        "mcp_execution_start",
        "MCPExecutionEnd",
        "mcp_execution_end"
    };

    public static OperationRecord? ToAuditRecord(HookPayload payload, ulong sequence)
    {
        if (!RecordableEvents.Contains(payload.EventName))
        {
            return null;
        }

        var target = AuditTarget(payload);
        var detail = AuditDetail(payload, target);
        if (string.IsNullOrWhiteSpace(target) && string.IsNullOrWhiteSpace(detail))
        {
            return null;
        }

        return new OperationRecord
        {
            Id = $"hook_{payload.SessionId}_{payload.EventName}_{sequence}_{Guid.NewGuid():N}",
            Timestamp = DateTimeOffset.Now,
            AgentName = string.IsNullOrWhiteSpace(payload.Agent) ? CoreText.AgentCenter : payload.Agent,
            OperationType = InferOperationType(payload, target, detail),
            TargetPath = string.IsNullOrWhiteSpace(target) ? detail : target,
            Detail = string.IsNullOrWhiteSpace(detail) ? target : detail,
            ProcessName = payload.ToolName,
            ToolInfo = payload.SessionId
        };
    }

    public static string AuditTarget(HookPayload payload)
    {
        var direct = payload.String(["target_path", "targetPath", "file_path", "filePath", "path", "command"]);
        if (!string.IsNullOrWhiteSpace(direct))
        {
            return direct;
        }

        var fromInput = ExtractFromJson(payload.ToolInput, ["file_path", "filePath", "path", "target_path", "targetPath", "command"]);
        if (!string.IsNullOrWhiteSpace(fromInput))
        {
            return fromInput;
        }

        var lowerTool = payload.ToolName.ToLowerInvariant();
        if (lowerTool.Contains("shell", StringComparison.Ordinal) ||
            lowerTool.Contains("bash", StringComparison.Ordinal) ||
            lowerTool.Contains("exec", StringComparison.Ordinal) ||
            lowerTool.Contains("command", StringComparison.Ordinal))
        {
            return payload.ToolInput;
        }

        return payload.Cwd;
    }

    private static string AuditDetail(HookPayload payload, string target)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(payload.EventName)) parts.Add(payload.EventName);
        if (!string.IsNullOrWhiteSpace(payload.ToolName)) parts.Add(payload.ToolName);
        if (!string.IsNullOrWhiteSpace(payload.Status)) parts.Add(payload.Status);
        if (!string.IsNullOrWhiteSpace(payload.ToolInput) && payload.ToolInput != target)
        {
            parts.Add(payload.ToolInput.Length > 240 ? payload.ToolInput[..240] : payload.ToolInput);
        }

        return string.Join(" | ", parts);
    }

    private static OperationType InferOperationType(HookPayload payload, string target, string detail)
    {
        var combined = $"{payload.EventName} {payload.ToolName} {target} {detail}".ToLowerInvariant();
        if (combined.Contains("delete", StringComparison.Ordinal) ||
            combined.Contains("remove", StringComparison.Ordinal) ||
            combined.Contains("trash", StringComparison.Ordinal) ||
            combined.Contains(" rm ", StringComparison.Ordinal) ||
            combined.Contains(" del ", StringComparison.Ordinal))
        {
            return OperationType.Delete;
        }

        if (combined.Contains("read", StringComparison.Ordinal) ||
            combined.Contains("view", StringComparison.Ordinal) ||
            combined.Contains("open", StringComparison.Ordinal) ||
            combined.Contains("grep", StringComparison.Ordinal) ||
            combined.Contains("rg ", StringComparison.Ordinal) ||
            combined.Contains("cat ", StringComparison.Ordinal) ||
            combined.Contains("type ", StringComparison.Ordinal) ||
            combined.Contains("ls ", StringComparison.Ordinal))
        {
            return OperationType.Read;
        }

        if (combined.Contains("shell", StringComparison.Ordinal) ||
            combined.Contains("exec", StringComparison.Ordinal) ||
            combined.Contains("command", StringComparison.Ordinal) ||
            combined.Contains("mcp", StringComparison.Ordinal))
        {
            return OperationType.Execute;
        }

        if (combined.Contains("write", StringComparison.Ordinal) ||
            combined.Contains("create", StringComparison.Ordinal) ||
            combined.Contains("new file", StringComparison.Ordinal))
        {
            return OperationType.Create;
        }

        if (combined.Contains("move", StringComparison.Ordinal)) return OperationType.Move;
        if (combined.Contains("rename", StringComparison.Ordinal)) return OperationType.Rename;
        return OperationType.Modify;
    }

    private static string ExtractFromJson(string value, IReadOnlyList<string> keys)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        try
        {
            var payload = HookPayload.Parse(value);
            return payload.String(keys);
        }
        catch
        {
            return "";
        }
    }
}
