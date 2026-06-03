using System.Text.Json;
using AgentGuard.Core.Localization;

namespace AgentGuard.Core.Services;

public sealed class HookPayload
{
    private readonly Dictionary<string, JsonElement> _values;

    private HookPayload(Dictionary<string, JsonElement> values)
    {
        _values = values;
    }

    public static HookPayload Parse(string line)
    {
        using var document = JsonDocument.Parse(line);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("Hook payload must be a JSON object.");
        }

        var map = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            map[property.Name] = property.Value.Clone();
        }

        if (!map.ContainsKey("event"))
        {
            if (map.TryGetValue("hook_event_name", out var hookEventName) ||
                map.TryGetValue("hookEventName", out hookEventName))
            {
                map["event"] = hookEventName.Clone();
            }
        }

        return new HookPayload(map);
    }

    public string EventName => String(["event", "hook_event_name", "hookEventName"]);

    public string SessionId => String(["session_id", "sessionId", "conversation_id", "conversationId", "id"]);

    public string Agent => String(["agent", "source_label", "sourceLabel", "provider"], CoreText.AgentCenter);

    public string Cwd => String(["cwd", "working_directory", "workingDirectory", "project_path", "projectPath"]);

    public string Project => ProjectName(Cwd, EventName switch
    {
        "PermissionRequest" or "permission_request" => CoreText.ApprovalRequest,
        "AskQuestion" or "ask_question" => CoreText.QuestionRequest,
        "PlanApproval" or "plan_approval" => CoreText.PlanApproval,
        _ => CoreText.AgentSession
    });

    public string ToolName => String(["tool", "tool_name", "toolName", "name"], EventName);

    public string ToolInput => String(["tool_input", "toolInput", "input", "arguments", "args"]);

    public string Diff => String(["diff"]);

    public string Status => String(["status"]);

    public string Terminal => String(["tty", "terminal"]);

    public string Question => String(["question", "prompt", "message"]);

    public string Header => String(["header", "title"]);

    public string ResponseMode => String(["response_mode", "responseMode"]);

    public string PlanTitle => String(["plan_title", "planTitle", "title"], CoreText.Plan);

    public string PlanContent => String(["plan_content", "planContent", "content", "plan"]);

    public List<string> Options => StringArray(["options"]);

    public List<string> Descriptions => StringArray(["descriptions"]);

    public List<string> RequestedPermissions => StringArray(["requested_permissions", "requestedPermissions", "permissions"]);

    public bool MultiSelect => Bool(["multi_select", "multiSelect"]);

    public string String(IEnumerable<string> keys, string fallback = "")
    {
        foreach (var key in keys)
        {
            if (!_values.TryGetValue(key, out var value))
            {
                continue;
            }

            return value.ValueKind switch
            {
                JsonValueKind.String => value.GetString() ?? fallback,
                JsonValueKind.Number => value.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.Object or JsonValueKind.Array => value.GetRawText(),
                _ => fallback
            };
        }

        return fallback;
    }

    public string? NullableString(IEnumerable<string> keys)
    {
        var value = String(keys);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    public Dictionary<string, object?> ToSimpleDictionary()
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in _values)
        {
            result[pair.Key] = ToObject(pair.Value);
        }

        return result;
    }

    private List<string> StringArray(IEnumerable<string> keys)
    {
        foreach (var key in keys)
        {
            if (!_values.TryGetValue(key, out var value))
            {
                continue;
            }

            if (value.ValueKind == JsonValueKind.Array)
            {
                return value.EnumerateArray()
                    .Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() ?? "" : item.GetRawText())
                    .Where(item => !string.IsNullOrWhiteSpace(item))
                    .ToList();
            }

            if (value.ValueKind == JsonValueKind.String)
            {
                var text = value.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return [text];
                }
            }
        }

        return [];
    }

    private bool Bool(IEnumerable<string> keys)
    {
        foreach (var key in keys)
        {
            if (!_values.TryGetValue(key, out var value))
            {
                continue;
            }

            if (value.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                return value.GetBoolean();
            }

            if (value.ValueKind == JsonValueKind.String &&
                bool.TryParse(value.GetString(), out var parsed))
            {
                return parsed;
            }
        }

        return false;
    }

    private static object? ToObject(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number when value.TryGetInt64(out var number) => number,
            JsonValueKind.Number => value.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Array => value.EnumerateArray().Select(ToObject).ToList(),
            JsonValueKind.Object => value.EnumerateObject().ToDictionary(p => p.Name, p => ToObject(p.Value), StringComparer.OrdinalIgnoreCase),
            _ => null
        };
    }

    private static string ProjectName(string cwd, string fallback)
    {
        if (string.IsNullOrWhiteSpace(cwd))
        {
            return fallback;
        }

        try
        {
            return Path.GetFileName(cwd.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        }
        catch
        {
            return fallback;
        }
    }
}
