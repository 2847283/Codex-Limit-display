# Codex Limit Display

一个轻量的 Windows 桌面额度卡片，从本机 Codex 会话记录中读取最近一次结构化额度快照，显示剩余比例、重置时间和数据新鲜度。

它适合在长时间运行 Codex 任务时快速查看额度概况。数据来自本地快照，不代表逐 Token 或服务器实时计量。

## 功能

- 显示 Codex 主额度窗口和次额度窗口；窗口数量按实际数据动态生成。
- 将 `used_percent` 转换为剩余百分比，并显示对应重置时间。
- 监听当日与前一日的 Codex JSONL 会话记录，自动刷新最新快照。
- 标记 Codex 是否运行、数据是否过时，以及额度是否低于警戒值。
- 支持左键拖动、跨启动记忆位置、右键立即扫描和正常退出。
- 按 `Win+D` 返回桌面时恢复可见；使用普通应用时不保持置顶。
- 不显示任务栏按钮，不安装服务，不注册开机启动。

## 运行

### 从源码启动

需要 Windows x64 和 .NET 9 SDK：

```powershell
git clone https://github.com/2847283/Codex-Limit-display.git
cd Codex-Limit-display
dotnet run --project src/CodexQuotaWidget/CodexQuotaWidget.csproj
```

### 发布单文件版本

```powershell
dotnet publish src/CodexQuotaWidget/CodexQuotaWidget.csproj `
  --configuration Release `
  --runtime win-x64 `
  --self-contained true `
  --output publish `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true
```

生成的 `publish/CodexQuotaWidget.exe` 是自包含 Windows x64 单文件程序，目标机器无需另外安装 .NET。`publish/` 属于本地构建输出，不提交到 Git。

## 操作

- 左键按住卡片并拖动：移动卡片。
- 右键 → **立即扫描**：立即重新读取最新额度快照。
- 右键 → **退出**：保存位置、停止文件监听并正常结束进程。

位置保存在：

```text
%LOCALAPPDATA%\CodexQuotaWidget\settings.json
```

如果保存的位置超出当前虚拟桌面，程序会在下次启动时将卡片限制到可见区域。

## 数据来源

程序只读取当前用户目录下的 Codex 会话记录：

```text
%USERPROFILE%\.codex\sessions\**\*.jsonl
```

只接受以下结构化事件：

```text
type = event_msg
payload.type = token_count
payload.rate_limits = {...}
```

剩余比例按 `100 - used_percent` 计算。额度窗口根据 `window_minutes` 动态命名，因此不会假定账号一定同时拥有“5 小时”和“每周”两个窗口。

## 桌面层级策略

Windows 11 的桌面宿主可能由 `Progman`、`WorkerW` 和 `SHELLDLL_DefView` 组成，具体结构会随系统版本变化。程序会检测这些句柄，但保持 WPF 卡片为正常顶层窗口：

- 桌面重新成为前台时，将卡片提升到普通 Z 序顶部。
- 普通应用成为前台时，将卡片降到该应用之后。
- 不使用永久置顶，也不把 WPF 渲染面强制重挂为 Explorer 子窗口。

这一策略保留透明圆角渲染和直接鼠标输入，同时避免卡片持续覆盖普通应用。

## 隐私与安全边界

- 不联网查询 OpenAI、ChatGPT 或其他服务。
- 不读取 `.codex\auth.json`、Cookie 或登录令牌。
- 不连接 Codex 私有 IPC，也不启动额外的 Codex CLI。
- 不解析、显示或上传任务正文。
- 不修改 Codex 本地会话记录。
- 快照超过 15 分钟后会明确标记为可能过时。

## 构建与测试

```powershell
dotnet restore CodexQuotaWidget.sln
dotnet test CodexQuotaWidget.sln --configuration Release
```

当前测试覆盖：

- JSONL 尾部读取、损坏/未完成记录和额度窗口解析。
- 当日与前一日扫描、文件追加监听和资源释放。
- 额度状态、临界值、过时状态与重置时间显示。
- 位置保存、损坏设置回退和屏幕范围约束。
- 桌面前后台切换时的 Z 序行为。
- WPF 渲染、右键扫描、位置保存和正常退出链路。

## 项目结构

```text
CodexQuotaWidget.sln
├─ src/CodexQuotaWidget/          WPF 应用、额度读取和桌面层级逻辑
├─ tests/CodexQuotaWidget.Tests/  xUnit 单元与 WPF 冒烟测试
├─ docs/superpowers/plans/        原始测试驱动实施计划
└─ README.md
```

技术栈：C#、.NET 9、WPF、System.Text.Json、FileSystemWatcher、Win32 和 xUnit。
