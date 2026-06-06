# Agent data source formats (Windows) — research notes

## Claude Code
- Path: `%USERPROFILE%\.claude\projects\<encoded-path>\<session-uuid>.jsonl`
  - Path encoding: `C:\Users\foo` → `C--Users-foo` (drive letter uppercased, `:` and `\` → `-`)
- JSONL: each line is a message envelope
  - `type`: `user | assistant | attachment | system | tool_use | tool_result | progress | tool_use_summary`
  - For `user | assistant`: `message.content` is array of blocks
    - block `type`: `text | tool_use | image`
    - block `name`, `input` (with `file_path`, `command`, `content`, etc.)
  - Top-level: `cwd`, `sessionId`, `timestamp` (ISO8601), `parentUuid`
- Side files:
  - `~/.claude/file-history/<session>/<hash>@v1` - file edit snapshots
  - `~/.claude/tasks/<session>` - per-session task list
  - `~/.claude/plans/<session>` - plan files
  - `~/.claude/projects/<project>/<session>/subagents/<sub>.jsonl`
  - `~/.claude/projects/<project>/<session>/tool-results/<uuid>` - spillover
  - `~/.claude/history.jsonl` - `{timestamp, project, message}` (only user prompts)
  - `~/.claude/stats-cache.json` - aggregate token stats
- Tools to track: `Write | Edit | MultiEdit | Read | Bash | Glob | Grep | WebFetch | WebSearch | TodoWrite | Skill`

## Codex CLI
- Path: `%USERPROFILE%\.codex\sessions\YYYY\MM\DD\rollout-YYYY-MM-DDTHH-MM-SS-<uuid>.jsonl`
  - Flat files at root, plus year/month/day subdirs
- JSONL: each line is an event
  - `type`: `session_meta | turn_context | response_item | event_msg`
  - For `session_meta` / `turn_context`: `payload.cwd`, `payload.model`, `payload.model_provider`
  - For `response_item`: `payload.type` is `function_call | custom_tool_call | message | reasoning | web_search_call`
    - `function_call`: `payload.name`, `payload.arguments` (JSON string), `payload.call_id`
    - `custom_tool_call`: same shape, custom tools
    - `message`: text/assistant message
  - `timestamp` (ISO8601) at top level
- Sidecar SQLite:
  - `~/.codex/state_5.sqlite` - `threads` table (`id, created_at, cwd, title, model_provider`)
  - `~/.codex/logs_2.sqlite` - `logs` table with `feedback_log_body` containing function_call patterns (`codex.op=`, `function_call`, `tool_call`)

## Cursor (and Cursor-based: Trae, CodeBuddy, Windsurf, Roo Code)
- Path: `%APPDATA%\Cursor\User\workspaceStorage\<hash>\state.vscdb` (per workspace)
- `workspace.json` (sibling) has `folder` field with `file://` URI → project path
- Per-workspace `state.vscdb` schema:
  - `ItemTable` (key-value)
    - `ai-chat:sessionRelation:modelMap` - `{sessionId: {name, provider}}`
    - `ai-chat:sessionRelation:modeMap` / `planModeMap` / `specModeMap`
    - `icube_session_agent_map` - `{sessionId: agentType}`
    - `icube-ai-agent-storage-input-history` - `[{inputText, timestamp}]`
    - `memento/icube-ai-agent-storage` - `[{sessionId, isCurrent}]`
    - `cursorChat.ChatSessionStore.index` / `*ChatStore` - `entries: {sessionId: {title, lastMessage, createdAt, lastMessageDate}}`
    - `state.turnsHeight: {sessionId: count}`
- Global `state.vscdb` at `%APPDATA%\Cursor\User\globalStorage\state.vscdb`:
  - `cursorDiskKV` table, keys like `bubbleId:<composerId>:<bubbleId>` - JSON value with `{_v, type (1=user, 2=assistant), text, createdAt, isAgentic, toolResults, codeBlocks, allThinkingBlocks}`
- ObjectId date extraction: first 4 bytes = seconds since Unix epoch (MongoDB ObjectId format used for sessionIds)

## OpenClaw
- Path: `%USERPROFILE%\.openclaw\agents\<agent-name>\sessions\*.jsonl`
  - Plus flat: `%USERPROFILE%\.openclaw\state\session_*.jsonl` (per-session auto-save)
  - And: `%USERPROFILE%\.openclaw\state\**\*.jsonl` (recursively)
- JSONL: each line is an event
  - Top level: `timestamp`, optional `session_id`
  - `content`: array of blocks
    - `type`: `tool_use | tool_call | text | image`
    - `name`, `input` (dict with `file_path`, `command`, etc.)
  - `tool_calls`: array of `{function: {name, arguments (JSON string)}}`
  - `tool_call_id`
- Memory files at `~/.openclaw/memory/*.md` (long-term knowledge, not session data)
- Config files at `~/.openclaw/workspace/{SOUL.md,USER.md,AGENTS.md,IDENTITY.md,MEMORY.md,TOOLS.md}`
- QClaw uses `~/.qclaw/`, EasyClaw uses `~/.easyclaw/`

## Hermes Agent
- Sessions path: `%USERPROFILE%\.hermes\sessions\*.json`
  - One file per session
- Session JSON format:
  - `session_id` (UUID)
  - `model`, `platform` (e.g. "telegram", "cli", "webchat")
  - `session_start` (ISO8601)
  - `messages`: array
    - `role`: `user | assistant | system | tool`
    - `content` (string for text)
    - `tool_calls`: array
      - `function.name`
      - `function.arguments` (JSON string with `file_path`, `path`, `command`, `directory`)
- Sidecar SQLite: `~/.hermes/state.db` (or similar)
  - `sessions` table: `session_id, title, model, platform, started_at, ended_at`
- Skip `request_dump_*.json` files (debug dumps)
- Checkpoints at `~/.hermes/checkpoints/` are git-based, skip for now
- Memory at `~/.hermes/memories/*.md` (long-term knowledge, not session data)
