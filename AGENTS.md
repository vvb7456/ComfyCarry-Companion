# AGENTS.md — ComfyCarry Companion 项目交接文档

## 项目概述

ComfyCarry Companion 是一个 Windows 桌面客户端（WinUI 3），为 ComfyUI 远程 GPU 实例提供两个独立功能：

1. **生成存储配置（Tab 1）**：为无头实例生成 rclone.conf（实例无浏览器无法做 OAuth，由本客户端代为完成）
2. **产物取回（Tab 2）**：通过 WebDAV 从实例拉取 ComfyUI 输出文件到本机

两者完全独立，不共享数据。

## 技术栈

- **框架**: WinUI 3 (Windows App SDK) + .NET 9
- **目标**: net9.0-windows10.0.19041.0, x64 only
- **打包**: Unpackaged EXE（WindowsPackageType=None）
- **语言**: C# 13, ImplicitUsings=enable
- **关键依赖**: H.NotifyIcon.WinUI 2.3.2（系统托盘）
- **外部工具**: rclone.exe（放在 Assets/ 目录，CI 自动下载，不入 git）

## 编译与运行

```powershell
# 编译（Debug）
dotnet build ComfyCarry.sln -c Debug

# 运行产物路径
src\ComfyCarry\bin\x64\Debug\net9.0-windows10.0.19041.0\ComfyCarry.exe

# 发布（CI 使用）
dotnet publish src/ComfyCarry/ComfyCarry.csproj -c Release -r win-x64 --self-contained true /p:WindowsAppSdkSelfContained=true /p:PublishSingleFile=false /p:EnableMsixTooling=true -o publish
```

**注意**：
- 运行前确保 `Assets/rclone.exe` 存在（本地开发需手动放置，CI 自动下载）
- 编译时如果 ComfyCarry.exe 正在运行会报 MSB3027 文件锁定错误，先 `Stop-Process -Name ComfyCarry`
- PowerShell 不支持 `&&`，用 `;` 分隔命令

## 项目结构

```
src/ComfyCarry/
├── Models/
│   ├── ApiContracts.cs      # 面板 API 请求/响应模型
│   ├── CloudTypeDefs.cs     # 云盘类型定义（含 AutoAnswers/LogoPath）
│   ├── PanelInstance.cs     # 面板实例模型
│   ├── PullRule.cs          # 拉取规则（客户端本地所有）
│   ├── RcloneConfigState.cs # rclone 状态机模型
│   └── RcloneLogEntry.cs    # rclone JSON 日志解析
├── Services/
│   ├── AppPaths.cs          # 数据目录布局 (%LOCALAPPDATA%\ComfyCarry\)
│   ├── CompanionApiClient.cs # 面板 HTTP API 客户端
│   ├── HeartbeatService.cs  # 周期心跳（含 rule_summaries）
│   ├── InstanceStore.cs     # 实例持久化 (data/instances.json)
│   ├── JobReporter.cs       # Job 事件回报面板
│   ├── LocalizationService.cs # 中英双语内联字典
│   ├── PullEngine.cs        # 后台 rclone 拉取引擎
│   ├── RcloneService.cs     # rclone.exe 调用封装
│   ├── RuleEngine.cs        # 规则状态管理（本地）
│   ├── RuleStore.cs         # 规则持久化 (data/rules.json)
│   ├── SecretStore.cs       # DPAPI 加密
│   ├── ServiceHub.cs        # DI 容器（所有服务单例）
│   ├── SettingsService.cs   # 设置持久化
│   └── TrayController.cs    # Win32 原生托盘菜单
├── Views/
│   ├── Wizard/              # Tab1 向导（3步：选类型→命名→授权配置）
│   ├── CloudHomePage.xaml   # Tab1 首页（已配置列表+导出）
│   ├── CloudSetupPage.xaml  # Tab1 容器（步骤条+Frame）
│   ├── PullPage.xaml        # Tab2 产物取回主页
│   ├── RuleEditDialog.xaml  # Tab2 规则编辑弹窗
│   ├── ConnectDialog.xaml   # 连接面板弹窗
│   └── SettingsPage.xaml    # 设置页
├── MainWindow.xaml          # 主窗口（NavigationView 三Tab）
├── Program.cs               # 自定义入口（DispatcherQueue 同步上下文）
└── StartupHelper.cs         # 开机自启
```

## 关键架构决策

### Tab 1: 生成存储配置
- Wizard 精简为 3 步（选类型→命名→授权与配置）
- 配置完成后自动：验证连接 → 建目录（可选）→ 合并到 app conf → 回首页
- rclone 状态机自动回答：`config_type` 用 `__first__`，`config_driveid` 用 `__exact:OneDrive (personal)__`
- 导出在 Home 页（合并所有 remote 为一个 rclone.conf）

### Tab 2: 产物取回
- **规则归客户端所有**（本地 data/rules.json），不再存面板
- 规则按 `instance_label` 分组（跨实例重部署存活）
- 面板只收心跳中的只读 `rule_summaries` + Job 汇报
- 过滤器由 `Content`(images/videos/all) + `Subdirs` 动态生成，不存原始表达式
- 面板 rules CRUD 端点已废弃（404），客户端不再调用

### 面板 API 契约
| 端点 | 用途 |
|------|------|
| POST /api/companion/connect | 密码换 api_key + dav_url |
| POST /api/companion/heartbeat | 心跳（含 rule_summaries） |
| POST /api/companion/jobs | 创建 Job |
| POST /api/companion/jobs/{id}/events | 追加事件（key/params 格式） |
| POST /api/companion/jobs/{id}/finish | 结束 Job（status: success/failed/partial/cancelled） |

## 已知陷阱与注意事项

### WinUI 3 特有
- **ContentDialog 必须设置 XamlRoot**：`new MyDialog { XamlRoot = this.XamlRoot }`，否则闪退
- **ListView 撑满宽度**：需要 `ItemContainerStyle` 设置 `HorizontalContentAlignment=Stretch`
- **ProgressRing 不活跃时仍占空间**：默认设 `Visibility="Collapsed"`，执行时改 Visible
- **XamlCompiler 对编码敏感**：中文弯引号 `""` 会导致编译崩溃，用 `「」` 替代

### 构建
- `Assets/rclone.exe` 在 .gitignore 中（75MB），csproj 用 `Condition="Exists(...)"` 条件引入
- CI 在 publish 后单独下载 rclone.exe 到输出目录
- 不要在 csproj Release 条件中设 `PublishTrimmed=true`（WinUI 3 不兼容）

### 网络
- rclone 进程通过环境变量 `HTTP_PROXY`/`HTTPS_PROXY` 走代理（Settings.Data.Proxy）
- 浏览器 OAuth 走系统代理，rclone token exchange 走 rclone 自己的代理设置
- 如果用户未配置代理，Google Drive 等 OAuth 的 token exchange 可能超时

### 进程管理
- `RunAsync` 中 rclone 退出后 `ReadToEndAsync` 可能 hang（孙进程持有管道）
- 已加 3 秒超时兜底：`Task.WhenAny(readAll, Task.Delay(3000))`
- 托盘菜单用 Win32 `TrackPopupMenu`（XAML Island 输入管线限制）

## 本地化

所有 UI 文案通过 `App.Hub.Locale.T("key")` 获取，内联在 `LocalizationService.cs` 的 Zh/En 字典中。

文案原则：简洁、不写废话、不用"远程实例""无头实例"等术语（用"ComfyCarry"或直接省略）。

## CI

GitHub Actions: `.github/workflows/build.yml`
- 触发: push to main
- 步骤: checkout → .NET 9 → restore → publish → 下载 rclone.exe → 上传 artifact
- 产物: `ComfyCarry-windows-x64`

## 数据文件位置

```
%LOCALAPPDATA%\ComfyCarry\
├── data/
│   ├── instances.json       # 面板实例列表
│   ├── rules.json           # 拉取规则（按 instance_label 分组）
│   ├── settings.json        # 用户设置
│   └── companion-rclone.conf # 内置 rclone 配置（WebDAV remote + Tab1 导出的 remote）
├── comfycarry.log           # 运行日志
└── wizard/                  # Tab1 向导临时 conf
```

## 当前版本状态

- v1.0.2
- Tab 1（生成存储配置）：功能完整，OneDrive/Google Drive 已验证
- Tab 2（产物取回）：刚完成重构，UI 已重建，需要连接真实面板实例冒烟测试
- 托盘菜单：Win32 原生实现，已验证
- CI：通过
