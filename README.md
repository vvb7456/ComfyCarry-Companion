# ComfyCarry Companion

把 ComfyUI 实例产物取回本机 Windows 桌面客户端 + 云存储 rclone.conf 配置助手。

基于 **WinUI 3 (Windows App SDK) + C# / .NET 8**，非打包 (unpackaged) 自包含发布，双击 `.exe` 即用，无需安装 .NET / MSIX。不用 Electron。

详细需求契约见 [SPEC.md](./SPEC.md)（§0 架构决策、§2 面板 API 契约、§3 客户端设计、§4 既有约定）。

---

## 功能概览

| Tab | 名称 | 阶段 | 核心 |
|---|---|---|---|
| 1 | 云存储配置 | 部署**前**（纯本地） | Fluent 分步向导配 6 种云，rclone 非交互状态机完成 OAuth，**导出 rclone.conf** 供面板 Wizard 上传 |
| 2 | 产物取回 | 部署**后**（连实例） | 面板 URL+密码连接 → 规则驱动内置 rclone 拉取到本机 → Job/心跳回报面板 |
| — | 设置 | — | 实例管理 / 语言 / 主题 / 开机自启 / 关窗到托盘 |
| — | 系统托盘 | 常驻 | 关窗最小化到托盘，后台继续按规则拉取；菜单：显示/暂停·继续/退出 |

支持云类型：OneDrive(个人/商业) · Google Drive · Dropbox · WebDAV · Cloudflare R2 · AWS S3。

---

## 工程结构

```
ComfyCarry.sln
src/ComfyCarry/
├─ ComfyCarry.csproj          # 工程文件（WindowsAppSDK / 非打包 / 自-contained win-x64）
├─ app.manifest               # DPI 感知、Windows 兼容性清单
├─ Program.cs                 # 入口 Main：单实例互斥 + WinUI 启动
├─ App.xaml(.cs)              # Application：装配 ServiceHub + 显示信号监听
├─ GlobalUsings.cs            # 全局 using
├─ MainWindow.xaml(.cs)       # NavigationView 三导航 + 窗口位置记忆 + 关窗到托盘
├─ StartupHelper.cs           # 开机自启（HKCU\...\Run 注册表）
├─ Assets/                    # 放 rclone.exe + app.ico（见 Assets/PLACEHOLDER.md）
├─ Models/                    # 数据模型
│  ├─ CloudTypeDefs.cs        # 6 种云类型定义（rclone type / 字段 / 是否 OAuth）
│  ├─ RcloneConfigState.cs    # rclone 非交互状态机返回的状态对象
│  ├─ PanelInstance.cs        # 一个连接的面板实例（DPAPI 加密凭据）
│  ├─ PullRule.cs             # 拉取规则（与面板 sync_rules 同源）
│  ├─ ApiContracts.cs         # connect/rules/heartbeat/jobs 的请求响应 DTO（字段照 SPEC §2）
│  └─ RcloneLogEntry.cs       # rclone --use-json-log 行 + WebDAV 条目
├─ Services/                  # 业务逻辑
│  ├─ ServiceHub.cs           # 统一装配所有服务单例
│  ├─ AppPaths.cs             # %LOCALAPPDATA%\ComfyCarry\ 目录布局
│  ├─ SecretStore.cs          # DPAPI (CurrentUser) 加解密
│  ├─ InstanceStore.cs        # 多实例列表存储 + 凭据加解密
│  ├─ SettingsService.cs      # 语言/主题/自启等偏好
│  ├─ LocalizationService.cs  # 中英双语（内联字典）
│  ├─ RcloneService.cs        # rclone 调用：状态机 OAuth / lsd / mkdir / obscure / pull / lsf
│  ├─ CompanionApiClient.cs   # 面板 JSON API + 401 自动重连刷新 api_key
│  ├─ JobReporter.cs          # Job 创建/事件/收尾回报
│  ├─ RuleEngine.cs           # 规则本地缓存 + 面板同步（面板为真源）
│  ├─ HeartbeatService.cs     # 周期心跳上报
│  ├─ PullEngine.cs           # 后台规则驱动 rclone 拉取执行
│  ├─ TrayController.cs       # 系统托盘（H.NotifyIcon.Windowless）
│  ├─ RelayCommand.cs         # 极简 ICommand
│  └─ SingleInstance.cs       # 单实例互斥锁
└─ Views/                     # UI 页面
   ├─ CloudSetupPage.xaml(.cs)   # Tab 1 主页 + 向导壳
   ├─ PullPage.xaml(.cs)         # Tab 2 主页
   ├─ SettingsPage.xaml(.cs)     # 设置页
   ├─ ConnectDialog.xaml(.cs)    # 连接实例对话框
   ├─ RuleEditDialog.xaml(.cs)   # 规则编辑对话框
   ├─ ArtifactListView.xaml(.cs)# 产物网格（WebDAV 列举）
   └─ Wizard/                    # Tab 1 向导各步
      ├─ WizardTypePage.xaml(.cs)   # 选云类型 + remote 名 + 代理
      ├─ WizardParamsPage.xaml(.cs) # 填参（非 OAuth）
      ├─ WizardOAuthPage.xaml(.cs)  # OAuth 状态机（OneDrive 选 drive 回灌）
      ├─ WizardTestPage.xaml(.cs)   # rclone lsd 测试
      ├─ WizardTreePage.xaml(.cs)   # 建标准目录树
      └─ WizardExportPage.xaml(.cs) # 导出 rclone.conf
```

---

## 在 Windows 上构建与发布

### 前置

- Windows 10 1809+ / Windows 11
- .NET 8 SDK（`winget install Microsoft.DotNet.SDK.8`）
- Windows App SDK 工作负载（VS 2022 17.8+ 或单独装 Windows App SDK）
- 把 **`rclone.exe`**（Windows amd64 版，https://rclone.org/downloads/）放到 `src/ComfyCarry/Assets/rclone.exe`
- （可选）把 `app.ico` 放到 `src/ComfyCarry/Assets/app.ico`

详见 [`src/ComfyCarry/Assets/PLACEHOLDER.md`](./src/ComfyCarry/Assets/PLACEHOLDER.md)。

### 还原与构建

```bat
cd src\ComfyCarry
dotnet restore
dotnet build -c Release
```

### 发布为双击即用的自包含 exe

```bat
dotnet publish -c Release -r win-x64 -p:WindowsPackageType=None -p:SelfContained=true -p:PublishSingleFile=false
```

关键工程属性（已在 `ComfyCarry.csproj` 配置）：

```xml
<WindowsPackageType>None</WindowsPackageType>
<WindowsAppSDKSelfContained>true</WindowsAppSDKSelfContained>
<SelfContained>true</SelfContained>
<RuntimeIdentifier>win-x64</RuntimeIdentifier>
```

产物位于 `src\ComfyCarry\bin\Release\net8.0-windows10.0.19041.0\win-x64\publish\`，包含 `ComfyCarry.exe`、`rclone.exe`（随包复制）、.NET 运行时与 Windows App SDK 依赖。

> 想做成真正的单文件，可加 `-p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true`，但 WinUI 3 单文件有已知坑，非必要不开启。

### 调试运行

```bat
dotnet run -c Debug
```

或用 Visual Studio 2022 打开 `ComfyCarry.sln`，F5 调试。

---

## 数据目录

运行时数据存放于 `%LOCALAPPDATA%\ComfyCarry\`：

| 文件 | 内容 |
|---|---|
| `data/instances.json` | 连接的实例列表（密码/api_key 经 DPAPI 加密） |
| `data/settings.json` | 语言/主题/自启等偏好 |
| `data/companion-rclone.conf` | Tab 2 为各实例生成的 webdav remote |
| `data/wizard/wizard-*.conf` | Tab 1 向导临时 conf |
| `comfycarry.log` | 运行日志 |

凭据（面板密码、api_key、WebDAV pass）一律经 **Windows DPAPI（CurrentUser）** 加密，不落明文。

---

## 面板 API 契约

客户端严格按 SPEC §2 调用以下端点：

| 端点 | 方法 | 用途 |
|---|---|---|
| `/api/companion/connect` | POST | 面板密码换 api_key + dav_url + dav_user |
| `/api/companion/rules?client_id=` | GET | 取本客户端规则 + 预设模板 |
| `/api/companion/rules` | POST | 新建/更新规则（面板为真源） |
| `/api/companion/rules/<id>` | DELETE | 删除规则 |
| `/api/companion/heartbeat` | POST | 周期上报 client_id/hostname/version/status/progress |
| `/api/companion/jobs` | POST | 创建 Job |
| `/api/companion/jobs/<id>/events` | POST | 追加事件（rule_start/file_transferred/rule_done/log） |
| `/api/companion/jobs/<id>/finish` | POST | Job 收尾（status/统计） |

401 处理：用密码重新 `connect` 刷新 api_key 后重试一次（`CompanionApiClient.SendWithAuthAsync`）。

---

## 已知未验证点清单（本机无 dotnet，未编译）

> 以下条目在 Linux 构建机无法验证，需在 Windows 端 `dotnet build` + 运行后逐一确认。

### 工程与依赖

1. **WindowsAppSDK 版本兼容**：`Microsoft.WindowsAppSDK 1.5.240602001` 与 `Microsoft.Windows.SDK.Build.Tools 10.0.26100.1742` 的组合是否在目标机正常 restore/打包；若装的是 1.4/1.6 需同步调整版本号。
2. **H.NotifyIcon.Windowless 2.0.107** 与 WindowsAppSDK 1.5 的兼容性（该库要求 WindowsAppSDK ≥ 1.2，理论 OK，但 `TaskbarIcon` 在 unpackaged 进程内创建托盘的具体行为需实机验证）。
3. **`Microsoft.Win32.Registry 5.0.0` / `System.Security.Cryptography.ProtectedData 8.0.0`** 在 net8.0-windows TFM 下能否正常还原（应为内置/兼容）。
4. **`DisableXamlGeneratedEntryPoint=true` + 自定义 `Program.Main`**：WinUI 3 的 XAML 代码生成器是否会冲突，需 build 后确认；若报重复 Main，改用 `DefineConstants;DISABLE_XAML_GENERATED_MAIN`。
5. **非打包 + 自包含发布**：`WindowsPackageType=None` + `SelfContained=true` + `RuntimeIdentifier=win-x64` 的 publish 产物是否真的双击即用（Windows App SDK 的 bootstrap 初始化在 unpackaged 模式需 `WindowsAppSDKSelfContained=true`，已设）。

### XAML / 数据绑定

6. **`x:Bind` 编译时绑定**：`PullPage`/`SettingsPage`/`ArtifactListView`/`WizardOAuthPage` 的 `x:DataType` 需类型在编译时可见；`PullRuleVM`/`InstanceVM`/`RemoteEntryVM`/`ConfigChoiceVM` 定义在 code-behind 同命名空间，应可见，但 `x:Bind` 对非 `INotifyPropertyChanged` 属性只绑定一次（进度/状态不动态刷新，见下条）。
7. **进度/状态不实时刷新**：`PullRuleVM.Progress`/`StatusText` 是普通属性，UI 通过 `RefreshRulesUI()` 重建集合刷新，非真正的双向绑定。拉取进行中的进度条不会平滑更新——需后续给 VM 实现 `INotifyPropertyChanged`（已列入二期）。
8. **`NavigationView` 顶部向导导航**：`CloudSetupPage` 内嵌 `NavigationView`（Top 模式）+ `Frame` 的页间跳转依赖 `this.Frame.Parent is NavigationView` 的层级假设，结构若调整需同步改。
9. **`ItemsRepeater` + `UniformGridLayout`**：产物网格在空数据/单条数据的布局表现。
10. **`ContentDialog` 的 `XamlRoot`**：`ConnectDialog`/`RuleEditDialog`/`RunOnce` 确认框用 `WinRT.Interop.InitializeWithWindow.Initialize(dlg, hwnd)` 挂到主窗口——unpackaged 模式下 `hwnd` 获取是否稳定。

### 托盘

11. **`GeneratedIconSource`**：H.NotifyIcon 的生成式图标（显示字母 "C"）是否在所有目标 Windows 版本正常渲染；若不渲染，改为放置 `app.ico` 并用字符串路径。
12. **`ContextFlyout` vs `ContextMenu`**：WinUI 3 用 `ContextFlyout`（已改），但 `MenuActivation=LeftOrRightClick` 的右键菜单行为需实机确认。
13. **关窗到托盘**：`AppWindow.Closing` 事件取消 + `ShowWindow(SW_HIDE)` 的组合是否在 Win11 上稳定隐藏（托盘图标仍可见）。
14. **单实例信号**：`EventWaitHandle` 跨进程通知"显示主窗口"——第二个实例发信号后首个实例是否可靠唤起窗口（依赖 DispatcherQueue.TryEnqueue）。

### rclone 交互

15. **非交互状态机 JSON 形状**：`RcloneConfigState` 的字段名（`State`/`Result`/`Choices`/`Options`）按 rclone `config create --all --non-interactive` 的实际输出建模，**未实机抓包验证**；若 rclone 版本输出字段名不同，需调整 `RcloneConfigState.cs` 与 `ParseState`。
16. **OneDrive 选 drive 的回灌**：`ConfigContinueAsync` 用 `--continue --state <s> --result <r>`，`result` 取自 `Choices[i]["Result"]` 或索引——实际 rclone 期望的 `result` 格式（字符串 vs 数字）需用真实 OneDrive 账号跑一遍确认。
17. **`rclone authorize` 浏览器唤起**：OAuth 类型调用 `config create` 时 rclone 是否自动开浏览器完成授权（与 `127.0.0.1:53682` 回调）——依赖 rclone 版本与系统默认浏览器设置。
18. **`--use-json-log` 行解析**：`RcloneLogEntry` 字段（`object`/`stats`/`percentage`/`speed`）对齐 rclone 文档，但不同 rclone 子命令的 JSON 字段可能有差异；`TryParseLog` 有兜底非 JSON 行，但不保证进度字段全准。
19. **`rclone obscure`**：用于 webdav remote 的 `pass` 字段——`rclone obscure <plain>` 的 stdout 即 obscured 值，需确认无多余换行。
20. **`rclone lsf --files-only`**：产物列表只列文件名，缩略图未实现（需带 basic auth 的 HttpClient 拉 WebDAV GET，二期补）。
21. **rclone.exe 缺失**：Tab1 显示警告条、Tab2 标记错误——但 `PullAsync` 内部 `IsPresent` 检查在 `RunStreamingAsync` 前已做，不会崩。

### DPAPI / 存储

22. **DPAPI 跨用户**：`ProtectedData.Protect(CurrentUser)` 加密的凭据在同一 Windows 用户账户下可解，换用户/换机不可解——预期行为，但需确认 `instances.json` 迁移场景。
23. **`instances.json` 序列化**：`PanelInstance` 的 `Password`/`ApiKey` 是 `[JsonIgnore]` 内存字段，`EncPassword`/`EncApiKey` 落盘——确认 JSON 不泄漏明文。

### 面板 API 契约

24. **`connect` 响应字段**：`ConnectResponse` 严格按 SPEC §2.2 的 `ok/api_key/dav_url/dav_user/comfyui_dir/instance_label`；若面板实现额外字段或字段名微调，需同步 `ApiContracts.cs`。
25. **`rules` 响应**：`RulesResponse` 假设 `{rules:[...], templates:[...]}`——SPEC §2.3 描述但未给精确 JSON，需对照面板 `companion.py` 实现确认 templates 是否在同响应。
26. **Job 字段**：`JobCreateRequest`/`JobEventRequest`/`JobFinishRequest` 按 SPEC §2.5 描述建模，需与面板 `sync_store` 的 job/event 表字段对齐。
27. **401 重连**：`SendWithAuthAsync` 重试一次——若面板密码已变更，第二次仍 401，UI 需引导重连（当前仅静默返回 401，未弹窗）。

### 其它

28. **Mica 背景**：`Microsoft.UI.Composition.SystemBackdrops.MicaBackdrop` 在 Win10 1809 无 Mica 时 try/catch 静默降级——但 `SystemBackdrop` 赋值本身在无 Mica 机的行为需确认。
29. **开机自启**：`StartupHelper` 写 `HKCU\...\Run`，unpackaged exe 路径含运行时目录，`Environment.ProcessPath` 是否稳定返回 exe 全路径。
30. **多实例 webdav remote 命名**：`cc-{inst.Id前8位}` —— 若两实例 Id 前 8 位撞名会覆盖（概率极低，但理论存在）。

---

## 约束与边界

- 本客户端**不连实例做云配置推送**（Tab 1 只产出 rclone.conf，由用户手动上传到面板 Wizard）。
- 拉取规则**以面板为真源**，客户端本地缓存 + UI 编辑后回写。
- 内置 `rclone.exe` 由 Tab 1/Tab 2 共用；缺失时给出明确提示而非崩溃。
- 不使用 Electron，不手动解析 OAuth token（让 rclone 自己写 conf，客户端只读回文件）。
