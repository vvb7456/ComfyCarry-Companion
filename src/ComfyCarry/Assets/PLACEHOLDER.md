# Assets 目录占位说明

本目录用于存放 ComfyCarry Companion 运行时所需的外部二进制与图标资源。
**本仓库不提交二进制文件**，请在 Windows 构建机/打包阶段把以下文件放入本目录。

## 需要放置的文件

### 1. `rclone.exe` —— Windows amd64 版 rclone

- **来源**：https://rclone.org/downloads/ 下载 `rclone-v1.xx.x-windows-amd64.zip`，解压后取其中的 `rclone.exe`。
- **放置位置**：`src/ComfyCarry/Assets/rclone.exe`
- **用途**：Tab 1 云存储配置（OAuth 状态机 / lsd / mkdir）、Tab 2 产物取回（copy/move/sync 拉取）共用同一个内置 rclone.exe（SPEC §0.2/§3.1/§3.3）。
- **csproj 配置**：`ComfyCarry.csproj` 已声明
  ```xml
  <None Update="Assets\rclone.exe">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  </None>
  ```
  发布时会自动复制到输出目录，运行时 `AppPaths.RcloneExePath` 指向 `{应用目录}/rclone.exe`。
- **缺失行为**：若 `rclone.exe` 不存在，Tab 1 会显示警告条（`cloud.rcloneMissing`），Tab 2 拉取会标记错误并回报面板。功能不崩溃。

### 2. `app.ico` —— 应用图标（可选）

- **来源**：自行设计或使用任意 `.ico` 格式图标。建议包含 16/32/48/256 多尺寸。
- **放置位置**：`src/ComfyCarry/Assets/app.ico`
- **用途**：应用窗口图标与托盘图标。当前托盘使用 H.NotifyIcon 的 `GeneratedIconSource`（显示字母 "C"）作为占位，**不依赖 app.ico 也能运行**。
- **替换占位**：若放置了 `app.ico`，把 `Services/TrayController.cs` 中的
  ```csharp
  IconSource = new GeneratedIconSource { Text = "C", ... }
  ```
  改为
  ```csharp
  IconSource = "Assets/app.ico"
  ```
  （H.NotifyIcon 的 `IconSource` 支持字符串路径。）
- **csproj 配置**：已声明 `Assets\app.ico` 的 `CopyToOutputDirectory=PreserveNewest`。

## 注意

- 本机（Linux 构建）无法放置/校验 Windows 二进制，上述文件需在 Windows 端放入后再 `dotnet publish`。
- 不要把 `rclone.exe` 提交到 git 仓库（体积大且随版本更新）；建议在发布脚本里下载并拷入。
