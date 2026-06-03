# AgentGuard Windows 项目审查报告

- 审查时间：2026-06-03
- 审查范围：`Win_AgentGuard` 仓库全量
- 审查人：Mavis（代码静态审查，未跑 `dotnet build`）
- 项目维护边界：以 `Win_AgentGuard` 为主维护仓库，MacCleaner / macOS 版仅作历史功能参考

---

## 一、总体判断

**能实现既定的 MVP 功能，但当前状态约 90% 完工度，存在 4 个必修 Bug 和 10+ 处需要打磨的问题。**

架构清晰、分层合理（Core / App / HookBridge / SmokeTest），需求文档中列出的 7 个 tab、24 个 Agent 集成、命令规则、敏感检测、进程轮询、文件监控全部有对应实现。

**但审批闭环是产品核心，而该闭环的端到端测试覆盖率几乎为零**——`tools/AgentGuard.SmokeTest` 只覆盖了 3 个 happy path，没有真正的 named pipe 端到端测试。

### 总体评分

| 维度 | 评分 | 说明 |
|------|------|------|
| 架构分层 | ★★★★★ | Core / App / HookBridge 分工清晰 |
| 需求覆盖率 | ★★★★★ | 需求文档"已完成"项 100% 落地 |
| 核心功能正确性 | ★★★☆☆ | 4 个 P0 bug 必修 |
| 健壮性 | ★★★☆☆ | 跨线程、原子写、监控重启等需要加固 |
| 测试覆盖 | ★☆☆☆☆ | smoke test 仅 3 个 happy path |
| UI / UX | ★★★★☆ | 7 个 tab 全在，缺指示灯和细节 |
| 可维护性 | ★★★★☆ | 代码可读，缺单元测试和 warnings-as-errors |

---

## 二、需求 vs 实现对照

| 需求项 | 实现位置 | 状态 |
|--------|---------|------|
| Agent Center 7 个 tab | `src/AgentGuard.App/MainWindow.xaml:77-281` | ✅ 全部到位 |
| Named pipe `\\.\pipe\agentguard-hook` | `src/AgentGuard.Core/Services/HookServer.cs:128-135` | ✅ |
| 15 类 hook 事件 | `src/AgentGuard.Core/Services/HookServer.cs:201-207` | ⚠️ 仅 switch 3 类审批事件，其他返回 `{ok:true}` 满足"非审批快速返回"要求 |
| `agentguard-bridge.exe`（stdin → pipe） | `src/AgentGuard.HookBridge/Program.cs` | ✅ |
| Bridge 失败写 `%APPDATA%\AgentGuard\logs\bridge.log` | `src/AgentGuard.HookBridge/Program.cs:40-49` | ✅ |
| 24 个 Agent 集成 + sentinel 标记 | `src/AgentGuard.Core/Services/AgentCatalog.cs` + `src/AgentGuard.Core/Services/HookInstaller.cs` | ✅ |
| 默认受保护目录（Desktop/Documents/Downloads） | `src/AgentGuard.Core/Services/GuardAnalyzer.cs:430-453` | ✅ |
| 黑/白/未分类命令规则 + 5 个默认规则 | `src/AgentGuard.Core/Models/GuardModels.cs:81-121` | ✅ |
| `FileSystemWatcher` 监控受保护目录 | `src/AgentGuard.Core/Services/WindowsOperationMonitor.cs:85-118` | ✅ |
| 进程轮询 2 秒 | `src/AgentGuard.Core/Services/WindowsOperationMonitor.cs:120-150` | ✅ |
| 忽略 `.git` `node_modules` `bin` `obj` `.tmp` `.log` | `src/AgentGuard.Core/Services/WindowsOperationMonitor.cs:235-244` | ✅ |
| 5 种告警类型 + 告警冷却 | `src/AgentGuard.Core/Services/GuardAnalyzer.cs` | ✅ |
| 敏感文件检测（env / key / credentials） | `src/AgentGuard.Core/Services/GuardAnalyzer.cs:256-288` | ✅ |
| 敏感内容扫描（API key、token 等） | `src/AgentGuard.Core/Services/GuardAnalyzer.cs:290-329` | ✅ |
| JSON 原子持久化（temp + replace） | `src/AgentGuard.Core/Services/JsonFileStore.cs:23-40` | ✅ |
| 兼容 macOS hook output 结构 | `src/AgentGuard.Core/Services/HookServer.cs:304-325` | ✅ `hookSpecificOutput` + `permissionDecision` 双字段 |
| Hook 安装可重复执行（不重复插入） | `src/AgentGuard.Core/Services/HookInstaller.cs:189-192, 271-280` | ✅ |

**结论**：需求文档 `PROJECT_REQUIREMENTS.md` 第三节"当前实现状态"中列出的"已完成"项 100% 落地。

---

## 三、必修 Bug（P0，4 个）

### Bug 1：空 `session_id` 会让审批闭环对不上

**严重度**：🔴 必修  
**影响范围**：核心审批链路

**位置**：
- `src/AgentGuard.Core/Services/HookServer.cs:189`
- `src/AgentGuard.Core/Services/SessionStore.cs:46-60`

**问题描述**：

事件进入时 `_sessionStore.ApplyEvent(payload)` 会用 `payload.SessionId` 去 seed 或查找 session；如果是空串，`SessionStore.SeedPendingSession` 会生成一个新 GUID 作为 session id（`SessionStore.cs:48-50`）。但同一个 payload 又被原样传去 `CreatePermissionRequest/CreateQuestionRequest/CreatePlanRequest`，`PendingHookRequest.SessionId` 保留了原始空串。

**后果**：

- `SessionStore` 中那个 session 的 `PendingPermission` 被正确设置
- 但 `HookServer._pending` 中 key 的是空串，UI 上看到的是空 session
- 用户点 Allow → `RespondAsync` 调 `_sessionStore.SetPendingPermission(waiter.Request.SessionId, null)`（空串），**清的是另一个 session 的状态**
- `PendingHookRequest.Id` 是 GUID 不受影响，agent 能收到响应；但 UI / 审计 / session 状态全错位

**触发条件**：任何不发送 `session_id` 的 hook payload（理论上 Claude Code、Codex 都会带，但自定义 agent 或异常场景可能不带）。

**修法**：把 `HookServer._pending` 的 key 也走 `SessionStore.SeedPendingSession` 的同款规则，确保 session id 在整个审批链路上保持一致。

---

### Bug 2：`StatusMessage` 跨线程赋值，可能炸 WPF

**严重度**：🔴 必修  
**影响范围**：WPF UI 稳定性

**位置**：
- `src/AgentGuard.App/ViewModels/MainViewModel.cs:52`
- `src/AgentGuard.Core/Services/HookServer.cs:49, 89, 144, 181, 229`

**问题描述**：

```csharp
// MainViewModel.cs:52
_hookServer.ServerMessage += (_, message) => StatusMessage = message;
```

`HookServer` 在 worker 线程触发 `ServerMessage` 事件，UI 绑定 `StatusMessage` 到 TextBlock。WPF 绑定对从非 UI 线程触发的 `INotifyPropertyChanged` 行为不一致——简单 string 属性多数时候能 work，但调用栈深一点可能抛 `InvalidOperationException: The calling thread cannot access this object`。

**对照**：`MainViewModel.Dispatch` 已存在，但只对 collection 类事件用了，`ServerMessage` 漏了。

**修法**：把 `ServerMessage` 也走 `Dispatch(action)`。

---

### Bug 3：原始事件文件无上限增长

**严重度**：🔴 必修  
**影响范围**：磁盘空间

**位置**：
- `src/AgentGuard.Core/Services/HookServer.cs:210-222`
- `src/AgentGuard.Core/Services/JsonFileStore.cs:42-46`

**问题描述**：

```csharp
// JsonFileStore.cs:42
public async Task AppendLineAsync(string path, string line, CancellationToken cancellationToken)
{
    await File.AppendAllTextAsync(path, line + Environment.NewLine, cancellationToken);
}
```

`raw-events.jsonl` 一条追加一条，永不截断。Agent 高频事件场景下，几个小时就能到 GB 级，把 `%APPDATA%` 撑爆。

**修法选项**：

- 方案 A：按大小 / 条数 rotate（例如每 50MB 或 10 万条切一个文件）
- 方案 B：直接去掉原始事件文件，统一走 `AuditLogService` 的 5000 条上限
- 方案 C：原始事件只保留在内存（如果只是用于 debug）

---

### Bug 4：YAML 卸载不是原子写，断电/被杀会损坏配置

**严重度**：🔴 必修  
**影响范围**：用户配置文件完整性

**位置**：`src/AgentGuard.Core/Services/HookInstaller.cs:119-120`

**问题描述**：

```csharp
var content = await File.ReadAllTextAsync(configPath, cancellationToken);
await File.WriteAllTextAsync(configPath, StripSentinelBlock(content), cancellationToken);
```

JSON 路径走的是 `WriteJsonAsync`（temp + replace，原子），YAML 路径直接 `WriteAllTextAsync`。如果用户在卸载到一半进程被杀，agent 的 `~/.trae/config.yaml` 就毁了。

**违反需求**：`PROJECT_REQUIREMENTS.md` 第 299 行明确要求"卸载/移除 hook 时不破坏用户原有配置"。

**修法**：YAML 也用 `JsonFileStore` 那套 temp + replace 原子写。

---

## 四、健壮性问题（P1，建议修）

### 4.1 `Window_Closed` 是 `async void`

**位置**：`src/AgentGuard.App/MainWindow.xaml.cs:21-24`

```csharp
private async void Window_Closed(object? sender, EventArgs e)
{
    await _viewModel.DisposeAsync();
}
```

`async void` 不一定能在进程退出前完成 disposal。HookServer 可能没干净停，下一次启动会卡在 named pipe 上 2 秒超时。

**修法**：改用 `App.OnExit` 同步等待，或在 `Window_Closed` 中 `_viewModel.DisposeAsync().GetAwaiter().GetResult()` 同步阻塞。

---

### 4.2 `AddProtectedDirectory` 不重启 monitor

**位置**：`src/AgentGuard.App/ViewModels/MainViewModel.cs:253-263`

新增受保护目录后用户还得手动点 Start Monitoring。状态栏里 "Restart monitor to include it immediately" 是裸 UX。

**修法**：
- 方案 A：`WindowsOperationMonitor` 监听 `GuardAnalyzer.Changed` 事件动态增删 watcher
- 方案 B：ViewModel 调一次 `StopMonitoring` + `StartMonitoring`

推荐方案 A，方案 B 会中断现有监控半秒。

---

### 4.3 `Process` 资源竞争（理论性，当前实现 OK）

**位置**：`src/AgentGuard.Core/Services/WindowsOperationMonitor.cs:132-150`

`Process.GetProcesses()` 后在 `finally` 里 `process.Dispose()`。代码做了，✅。但写法比较脆弱——若后续重构挪走 `Dispose()` 调用，会泄露句柄。`try/catch` 覆盖了 process 退出场景，✅。

**建议**：把枚举改为 `foreach (var process in Process.GetProcesses())` 后显式 `using`，让编译器强制资源释放。

---

### 4.4 `_lastStatsSave` 竞争

**位置**：`src/AgentGuard.Core/Services/GuardAnalyzer.cs:358-362`

```csharp
if (DateTimeOffset.Now - _lastStatsSave > TimeSpan.FromSeconds(30))
{
    _lastStatsSave = DateTimeOffset.Now;
    _ = _store.WriteAsync(_paths.HourlyStatsPath, HourlyStats);
}
```

多线程同时通过 30s 检查会写两次。原子写保证不损坏，但浪费 IO。

**修法**：加锁或用 `Interlocked.CompareExchange`。

---

### 4.5 WPF 没有"Server Running / Monitoring"指示灯

UI 上只看 `StatusMessage` 字符串判断运行状态。`MainViewModel.IsServerRunning` / `IsMonitoring` 已有，但没绑到按钮 `CanExecute`。

**修法**：把 `StartServerCommand.CanExecute = () => !IsServerRunning`、`StopServerCommand.CanExecute = () => IsServerRunning`；UI 加两个状态圆点。

---

### 4.6 SessionStore 跨线程字段写入

**位置**：`src/AgentGuard.Core/Services/SessionStore.cs`

`ApplyEvent`（hook server 线程）与 `SetPendingPermission/Question/Plan`（UI 线程）同时修改同一个 `SessionState` 对象，没有锁。WPF 通过完整集合替换 (`SyncSessions`) 规避了 UI 撕裂，但内存态可能短暂不一致。

**修法**：给 `SessionState` 关键字段加锁，或为 `SessionState` 实现 `INotifyPropertyChanged`，UI 走细粒度绑定。

---

## 五、代码质量 / 可维护性

### 5.1 缺乏单元测试

0 个 xUnit/NUnit 项目。`tools/AgentGuard.SmokeTest` 仅覆盖：
- `HookPayload.Parse` ✅
- `HookAuditMapper.ToAuditRecord` ✅
- `GuardAnalyzer.RecordObservedCommand` 黑名单 ✅

**缺少的测试**：
- `HookServer` 端到端（named pipe 收发、并发连接、取消）
- `SessionStore` 状态机（各种事件序列下的 phase 转移）
- `HookInstaller` 的 JSON / YAML 安装、卸载、sentinel 行为
- `GuardAnalyzer` 的批量检测、敏感检测、告警冷却、命令规则匹配
- 任何 viewmodel 测试

**建议**：把 `tools/AgentGuard.SmokeTest` 升级为正式的 xUnit 测试项目，至少补 HookServer 和 HookInstaller 的核心路径。

---

### 5.2 重复代码：`JsonSerializerOptions`

**位置**：`src/AgentGuard.Core/Services/HookInstaller.cs:336-338`

```csharp
var options = new JsonSerializerOptions(JsonFileStore.Options);
```

应该直接复用 `JsonFileStore.Options`。

---

### 5.3 旧式 C# 8 using 链式语法

**位置**：`src/AgentGuard.Core/Services/HookServer.cs:152-154`

```csharp
await using (pipe)
using (var reader = new StreamReader(pipe, ...))
await using (var writer = new StreamWriter(pipe, ...))
{
    ...
}
```

可以编译，但读起来绕。

**修法**：改为显式大括号或 `using var` 声明式：

```csharp
await using var pipe = new NamedPipeServerStream(...);
using var reader = new StreamReader(pipe, ...);
await using var writer = new StreamWriter(pipe, ...);
```

---

### 5.4 `TreatWarningsAsErrors=false`

**位置**：`Directory.Build.props:6`

长期项目建议至少打开 warnings as errors，避免警告堆积。

---

### 5.5 Bridge 路径不可配置

**位置**：`src/AgentGuard.App/ViewModels/MainViewModel.cs:352-356`

```csharp
private static string? BridgeExecutablePath()
{
    var path = Path.Combine(AppContext.BaseDirectory, "agentguard-bridge.exe");
    return File.Exists(path) ? path : null;
}
```

如果 bridge 在别的位置，UI 没入口传参。`AgentRegistryService.InstallHooksAsync` 接受参数，但 ViewModel 拿不到。

**修法**：在 Settings tab 加一个"Hook Bridge 路径"输入框，存到 `%APPDATA%\AgentGuard\config.json`。

---

## 六、UI / UX 评估

### 优点
- 7 个 tab 全部对应需求（Approvals / Audit / Integrations / Sessions / Command Rules / Alerts / Settings）
- Top 5 状态卡数据来源正确（`MainViewModel.cs:138-143`）
- 详情面板在 Approvals 右侧直接展开
- 选中 Pending 后"Question / Reason"输入框自动清空（`SelectedPendingRequest` setter 95-97 行）
- 表格风格统一（`App.xaml` 集中 style）

### 缺点
- 没有 "Refresh" 自动周期，所有刷新依赖事件触发
- 没有"待处理超过 X 分钟"高亮
- 没有"批量允许同 Agent"按钮
- 没有导出审计报告按钮（需求 P1 提到）
- 状态栏只是文本，缺一个 dot 指示器
- 按钮缺 tooltip

---

## 七、按需求"P0 验证"清单的可执行性

`PROJECT_REQUIREMENTS.md` 第 271-300 行列的验收清单：

| 验收项 | 状态 | 备注 |
|--------|------|------|
| `dotnet restore` / `dotnet build` 成功 | ⚠️ 未验证 | macOS 上无 SDK，csproj 配置正确应能过 |
| `dotnet run --project src/AgentGuard.App` 启动 | ⚠️ 未验证 | macOS 上 WPF 不能跑，需 Windows 真机 |
| 发送 PermissionRequest 后 Approvals 出现数据 | ✅ 代码闭环 | Bug 1 会让 session 状态错位 |
| 点击 Allow/Deny hook 调用方收到 decision | ✅ | `HookServer.RespondAsync` + `BuildResponse` |
| AskQuestion 回答后收到 answer | ✅ | |
| PlanApproval 收到 mode+message | ✅ | |
| Pre/PostToolUse 进入 Audit | ✅ | via `HookAuditMapper` |
| 黑名单命令触发 Alerts | ✅ | via `GuardAnalyzer` |
| 受保护目录变更进入 Audit | ✅ | via `WindowsOperationMonitor.RecordFileEvent` |
| Integrations 显示状态 | ✅ | |
| 重复安装不重复插入 | ✅ | via `RemoveAgentGuardEntries` + sentinel strip |

**结论**：核心功能可以跑通，但 Bug 1 必须先修，否则审批闭环在边缘场景下会出错。

---

## 八、修复优先级

| 优先级 | 项 | 工作量 | 风险 |
|--------|---|--------|------|
| **P0** | Bug 1：空 session_id 对齐 | 30 分钟 | 中 |
| **P0** | Bug 2：StatusMessage 跨线程 | 10 分钟 | 低 |
| **P0** | Bug 3：raw-events 增长无界 | 1 小时 | 低 |
| **P0** | Bug 4：YAML 卸载原子写 | 30 分钟 | 低 |
| P1 | AddProtectedDirectory 触发 monitor 重启 | 1 小时 | 低 |
| P1 | HookServer 端到端集成测试 | 2-3 小时 | 中 |
| P1 | IsServerRunning / IsMonitoring 按钮可用性 | 1 小时 | 低 |
| P1 | SessionStore 线程安全 | 1 小时 | 中 |
| P2 | Bridge 路径可配置 UI | 1 小时 | 低 |
| P2 | 状态指示灯 | 2 小时 | 低 |
| P2 | 升级 smoke test 为正式单元测试项目 | 持续 | 中 |
| P3 | 打开 warnings as errors | 10 分钟 | 低 |
| P3 | 重构 C# 8 using 链式语法 | 1 小时 | 低 |

**P0 累计工作量**：约 2.5 小时  
**P0 + P1 累计工作量**：约 9-10 小时

---

## 九、TL;DR

**功能上能实现**——架构正确、需求映射完整、24 个 agent + 5 个命令规则 + 5 类告警 + 7 个 tab 全部对得上，HookBridge 简单可靠，JSON 持久化原子。

**工程上还有 4 个真 bug** 和 10+ 处需要打磨。最大的风险是：**smoke test 只测了 3 个 happy path，而整个产品价值在于"审批不漏、响应不串、状态不乱"**——这些全都没有自动化测试。

P0 那 4 个 bug 修完之后，建议补一轮集成测试，再上 Windows 真机跑一遍 README 中的 `agentguard-bridge` 样例。

---

## 附录 A：项目结构

```
Win_AgentGuard/
├── AgentGuard.Windows.sln              # 解决方案文件（4 个项目）
├── Directory.Build.props               # 全局编译配置
├── .gitignore
├── README.md
├── docs/
│   ├── PROJECT_REQUIREMENTS.md         # 需求文档
│   └── PROJECT_AUDIT.md                # 本文件
├── scripts/
│   ├── build.ps1                       # dotnet restore + build + smoke test
│   └── install-hooks.ps1               # 触发 AgentGuard.SmokeTest 安装 hook
├── src/
│   ├── AgentGuard.App/                 # WPF 桌面端（net8.0-windows）
│   │   ├── App.xaml / App.xaml.cs
│   │   ├── MainWindow.xaml / MainWindow.xaml.cs
│   │   └── ViewModels/                 # ObservableObject + RelayCommand + MainViewModel
│   ├── AgentGuard.Core/                # 核心服务（net8.0）
│   │   ├── Models/                     # AgentModels / GuardModels / SessionModels
│   │   └── Services/                   # 12 个服务类
│   └── AgentGuard.HookBridge/          # bridge 桥接器（net8.0）
│       └── Program.cs
└── tools/
    └── AgentGuard.SmokeTest/           # 命令行冒烟测试（net8.0）
        └── Program.cs
```

## 附录 B：关键文件清单

| 文件 | 行数 | 职责 |
|------|------|------|
| `src/AgentGuard.Core/Services/HookServer.cs` | 357 | Named pipe 服务端，审批状态机 |
| `src/AgentGuard.Core/Services/HookInstaller.cs` | 342 | Agent hook 注入（JSON / YAML / Plugin） |
| `src/AgentGuard.Core/Services/GuardAnalyzer.cs` | 505 | 告警 / 命令规则 / 受保护目录 / 敏感检测 / 小时统计 |
| `src/AgentGuard.Core/Services/WindowsOperationMonitor.cs` | 265 | FileSystemWatcher + 进程轮询 |
| `src/AgentGuard.Core/Services/SessionStore.cs` | 286 | 会话状态机 |
| `src/AgentGuard.Core/Services/AuditLogService.cs` | 63 | 审计日志 |
| `src/AgentGuard.Core/Services/AgentCatalog.cs` | 212 | 24 个 Agent 集成清单 |
| `src/AgentGuard.Core/Services/HookPayload.cs` | 209 | 跨命名约定的 JSON payload 解析 |
| `src/AgentGuard.Core/Services/HookAuditMapper.cs` | 157 | Hook 事件 → 审计记录 |
| `src/AgentGuard.Core/Services/JsonFileStore.cs` | 47 | 原子 JSON 持久化 |
| `src/AgentGuard.App/MainWindow.xaml` | 284 | 7 个 tab + 状态栏 |
| `src/AgentGuard.App/ViewModels/MainViewModel.cs` | 357 | 主 ViewModel |
| `src/AgentGuard.HookBridge/Program.cs` | 50 | stdin → named pipe |
| `tools/AgentGuard.SmokeTest/Program.cs` | 84 | 3 个 happy path 测试 |

合计约 3,300 行 C# 代码（不含 XAML / csproj / 脚本）。
