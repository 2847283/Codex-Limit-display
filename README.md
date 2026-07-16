# Codex Limit Display

一个计划中的 Windows 桌面组件，用于在 Codex 执行任务期间显示最近一次本地额度快照。

> 当前状态：仓库已完成初始化和技术方案设计，尚未提供可运行版本。

## 项目目标

Codex 会在本地任务记录中写入结构化的额度快照。本项目计划监听这些记录，并以桌面卡片形式显示：

- 当前额度窗口的剩余百分比
- 额度重置时间
- 快照最后更新时间
- Codex 运行与数据新鲜度状态

组件面向“运行任务时大致观察还剩多少额度”的场景，不承诺逐 Token 或服务器实时刷新。

## 工作方式

组件只读取当前用户目录中的 Codex 会话记录：

```text
%USERPROFILE%\.codex\sessions\**\*.jsonl
```

只接受以下结构化事件：

```text
type = event_msg
payload.type = token_count
payload.rate_limits = {...}
```

剩余比例由 `100 - used_percent` 得到。额度窗口根据 `window_minutes` 动态显示，因此不会假定账号一定同时拥有“5 小时”和“每周”两个窗口。

## 隐私与安全边界

- 不联网查询 OpenAI 或 ChatGPT。
- 不读取 `.codex\auth.json`、Cookie 或登录令牌。
- 不连接 Codex 私有 IPC。
- 不附带或启动额外的 Codex CLI。
- 不解析或显示任务正文。
- 不修改 Codex 的本地记录。
- 快照超过 15 分钟后明确标记为可能过时。

## 计划中的技术栈

- C# / .NET 9
- WPF
- `System.Text.Json`
- `FileSystemWatcher`
- Win32 桌面窗口挂载
- xUnit

最终目标是发布为 Windows x64 自包含便携 EXE，不安装系统服务，也不注册开机启动。

## 计划中的界面

- Codex 风格的黑灰色桌面卡片
- 动态额度窗口和剩余进度条
- 重置时间及最后更新时间
- 绿色、灰色和红色状态提示
- 支持拖动、记忆位置、立即扫描和退出
- 不显示任务栏按钮或托盘图标

## 开发计划

完整的测试驱动实施步骤见：

[Codex Quota Widget Implementation Plan](docs/superpowers/plans/2026-07-16-codex-quota-widget.md)

计划涵盖工程初始化、JSONL 解析、会话监听、WPF 界面、桌面层挂载、位置保存、测试和便携发布。

## 当前仓库内容

```text
README.md
docs/superpowers/plans/2026-07-16-codex-quota-widget.md
```

应用源码将在后续实现任务中按照计划加入。
