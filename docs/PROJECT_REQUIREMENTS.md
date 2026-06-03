# AgentGuard Windows 项目需求

更新时间：2026-06-03

## 维护边界

从现在开始，Windows 版本以 `Win_AgentGuard` 为主维护仓库。MacCleaner / macOS 版只作为历史功能参考，不再作为 Windows 版的开发落点，避免两边代码互相污染。

后续所有 Windows 相关改动优先在本项目内完成：

- 功能需求、验收标准、实现说明放在 `docs/`
- WPF 桌面端放在 `src/AgentGuard.App`
- 核心服务和数据模型放在 `src/AgentGuard.Core`
- Agent hook 命令行桥接器放在 `src/AgentGuard.HookBridge`
- 构建、安装、冒烟验证脚本放在 `scripts/` 和 `tools/`

## 产品目标

AgentGuard Windows 是一个原生 Windows 桌面应用，用来监控和管理本机 AI Agent 的高风险行为。核心目标是让用户能看到、审批、审计并追踪 AI Agent 对本机文件、命令和会话的操作。

产品必须保持三个原则：

- 原生桌面体验：使用 WPF / .NET，不使用 Electron 或网页壳。
- 本地优先：审批、审计、配置和告警数据默认保存在本机 `%APPDATA%\AgentGuard`。
- 审批链路优先可靠：hook 输入、SessionStore、审批队列、UI 响应、hook 返回必须闭环，不能出现“审批页没有数据但 agent 正在等待”的断链。

## 目标用户

- 在 Windows 上使用 Codex、Claude Code、Cursor、Gemini、Trae、Qoder、CodeBuddy、OpenCode 等 AI 编程 Agent 的用户。
- 希望对 AI Agent 的写文件、删文件、执行命令、计划审批、问题确认进行可视化管控的用户。
- 希望保留本地审计记录，定位某个 Agent 做过什么操作的开发者或团队。

## 核心场景

1. 用户打开 AgentGuard，应用自动启动本地 hook server。
2. Agent 发起 `PermissionRequest`、`AskQuestion` 或 `PlanApproval`。
3. AgentGuard 在审批页展示请求，并提供允许、拒绝、回答、接受计划、取消计划等动作。
4. 用户操作后，AgentGuard 通过 named pipe 把兼容响应返回给 Agent。
5. 所有 hook 事件、命令、文件监控事件进入审计记录。
6. 敏感文件、批量删除、受保护目录变更、黑名单命令触发告警。
7. 用户可查看 Agent 集成状态并安装/刷新 hook。

## 当前 MVP 功能

### 1. Agent Center

必须提供以下页面：

- Approvals：默认第一个 tab，展示待审批、待回答、待计划确认请求。
- Audit：展示最近本地审计记录。
- Integrations：展示支持的 Agent、安装状态和 hook 安装入口。
- Sessions：展示 Agent 会话状态、当前阶段、最后工具调用。
- Command Rules：展示黑名单、白名单、未分类命令规则和调用统计。
- Alerts：展示敏感文件、批量操作、受保护目录、进程启动等告警。
- Settings：配置受保护目录，启动/停止本地监控。

### 2. Hook Server

Windows 版使用 named pipe：

```text
\\.\pipe\agentguard-hook
```

必须支持一行一个 JSON hook 事件。重点事件：

- `PermissionRequest`
- `AskQuestion`
- `PlanApproval`
- `PreToolUse`
- `PostToolUse`
- `PostToolUseFailure`
- `ShellExecutionStart`
- `ShellExecutionEnd`
- `MCPExecutionStart`
- `MCPExecutionEnd`
- `SessionStart`
- `SessionEnd`
- `Stop`
- `Notification`
- `TokenUsage`

非审批类事件必须快速返回轻量 ack，避免阻塞 Agent：

```json
{ "ok": true }
```

审批类事件必须等待 UI 响应，并返回兼容 macOS 版 hook output 的结构。

### 3. HookBridge

`agentguard-bridge.exe` 是 Agent 配置里调用的入口。它必须：

- 从 stdin 读取 hook JSON。
- 连接 `agentguard-hook` named pipe。
- 把 JSON 转发给 AgentGuard。
- 把 AgentGuard 返回值写到 stdout。
- App 未启动时快速失败并写入 `%APPDATA%\AgentGuard\logs\bridge.log`。

### 4. Agent 集成

当前支持检测和 hook 配置的 Agent：

- Claude Code
- Codex
- Gemini CLI
- Cursor / Cursor CLI
- GitHub Copilot
- Trae / Trae CLI / Trae CN
- Qoder / Qoder CLI
- CodeBuddy / CodeBuddy CN
- Qwen
- Kimi
- DeepSeek
- OpenCode
- Factory / Droid
- StepFun
- AntiGravity
- WorkBuddy
- Hermes
- Kiro
- OpenClaw / QClaw / EasyClaw / AutoClaw

JSON 配置必须使用 AgentGuard 标记清理旧 hook，再写入新 hook。YAML 配置必须使用 sentinel block：

```text
# [AGENTGUARD-START]
# [AGENTGUARD-END]
```

### 5. 审计与告警

必须记录以下审计字段：

- 时间
- Agent 名称
- 操作类型
- 目标路径或命令
- 详情
- 进程/工具名
- Session ID 或来源信息

必须支持以下告警：

- 批量删除
- 批量修改
- 敏感文件访问
- 敏感内容写入
- 受保护目录变更
- Agent 进程启动
- 黑名单命令命中

### 6. 命令规则

命令规则分三类：

- Blacklist：高风险命令，触发阻断/告警。
- Whitelist：低风险命令，记录但不告警。
- Unclassified：新发现或未归类命令。

默认规则应包含 Windows 和跨平台高风险命令，例如：

- `rm -rf`
- `del /s /q`
- `Remove-Item`

### 7. Windows 本地监控

当前 MVP 使用 `FileSystemWatcher` 监控受保护目录，并轮询进程列表识别活跃 Agent。

要求：

- 默认监控 Desktop、Documents、Downloads。
- 支持用户新增受保护目录。
- 忽略 `.git`、`node_modules`、`bin`、`obj`、临时日志等低价值路径。
- 进程识别关键词必须覆盖 Codex、Claude、Cursor、Trae、CodeBuddy、OpenClaw、Hermes 等常见 Agent。

限制：

- `FileSystemWatcher` 不能精确归因“哪个进程写了哪个文件”。
- 后续如需更强归因，应接入 ETW；如需拦截级文件保护，应考虑 minifilter 驱动。

## 非功能需求

### 性能

- UI 首屏必须直接进入可用操作台，不做营销页。
- 审计列表默认展示最近 500 条，内存保留上限可高于展示上限。
- 文件监控必须过滤低价值目录，避免大型仓库频繁变更导致 UI 卡顿。
- JSON 持久化应批量/异步保存，避免阻塞 UI。

### 可靠性

- App 启动时自动加载历史状态、启动 hook server、启动本地监控。
- Hook server 停止时，挂起审批必须返回安全取消/拒绝响应。
- Hook 安装必须可重复执行，不应重复插入相同 AgentGuard hook。
- JSON/YAML 配置修改必须尽量保留用户原有配置。

### 隐私与安全

- 默认不上传审计、审批、路径、命令内容。
- 敏感内容扫描只读取必要文本文件，限制读取长度。
- Bridge 日志只记录失败原因，不记录完整敏感 payload。
- 后续打包发布时需要代码签名，避免 Windows 安全提示影响信任。

### 可维护性

- 业务模型和服务放在 `AgentGuard.Core`。
- WPF 只负责界面和用户交互。
- HookBridge 保持轻量，不承载复杂业务。
- 后续增加 Agent 时优先改 `AgentCatalog`，不要把 Agent 配置散落在 UI 层。

## 当前实现状态

已完成：

- WPF 主界面和 7 个功能 tab。
- Named pipe hook server。
- Pending approval/question/plan 队列。
- 兼容响应结构。
- HookBridge。
- Agent 集成清单和 hook 安装器。
- SessionStore。
- AuditLogService。
- GuardAnalyzer。
- WindowsOperationMonitor。
- JSON 本地持久化。
- `build.ps1` 和 smoke test 项目。

待 Windows 环境验证：

- `dotnet restore`
- `dotnet build`
- WPF 启动
- HookBridge 到 named pipe 的真实交互
- 真实 Agent 配置文件 hook 注入
- 真实 Windows 文件监控和进程识别

## 后续开发优先级

### P0：先让 MVP 在 Windows 真机跑通

- 在 Windows 安装 .NET 8 SDK。
- 执行 `scripts\build.ps1`。
- 启动 WPF 应用。
- 用 README 中的 hook JSON 样例验证审批能进入 UI 并返回响应。
- 对 Codex / Claude Code 至少各验证一次 hook 安装和事件回传。

### P1：产品可用性

- 增加 Windows toast 通知。
- 增加系统托盘入口。
- 增加开机自启动设置。
- 增加导出审计报告。
- 增加命令规则编辑、禁用、删除、导入导出。

### P2：监控增强

- 接入 ETW，增强进程到文件操作的归因。
- 支持更精细的受保护目录策略。
- 支持按 Agent、项目、风险等级筛选审计。

### P3：发布交付

- 生成 MSIX 或 MSI 安装包。
- 配置代码签名。
- 增加自动更新。
- 增加发布版本号和 changelog。

## 验收清单

基础构建：

- `dotnet restore .\AgentGuard.Windows.sln` 成功。
- `dotnet build .\AgentGuard.Windows.sln -c Debug` 成功。
- `dotnet run --project .\src\AgentGuard.App\AgentGuard.App.csproj` 能启动。

审批闭环：

- 发送 `PermissionRequest` 后 Approvals tab 出现数据。
- 点击 Allow 后 hook 调用方收到 `decision: allow`。
- 点击 Deny 后 hook 调用方收到 `decision: deny` 和 reason。
- 发送 `AskQuestion` 后能回答并返回 `answer`。
- 发送 `PlanApproval` 后能返回 `mode` 和 `message`。

审计闭环：

- `PreToolUse` / `PostToolUse` 进入 Audit tab。
- Shell command 进入 Command Rules 统计。
- 黑名单命令触发 Alerts。
- 受保护目录文件变化进入 Audit tab。

集成闭环：

- Integrations tab 能显示 Agent 状态。
- Install hooks 后配置文件中出现 AgentGuard hook 标记。
- 重复安装不会重复插入 hook。
- 卸载/移除 hook 时不破坏用户原有配置。

## 明确不做

当前阶段不做：

- 不维护 MacCleaner 中的 Windows 版本副本。
- 不引入 Electron。
- 不默认上传用户本地审计数据。
- 不在 MVP 阶段开发内核驱动。
- 不把 Windows 版逻辑混进 macOS Swift 项目。
