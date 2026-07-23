# ComfyCarry Companion

Windows 桌面客户端，为 ComfyUI 远程 GPU 实例提供两个功能：

1. **同步** - 通过 WebDAV 从实例拉取 ComfyUI 输出文件到本机
2. **云存储配置** - 为无头实例生成 rclone.conf（代为完成 OAuth）

## 下载

前往 [Releases](https://github.com/vvb7456/ComfyCarry-Companion/releases) 下载 zip，解压后双击 `ComfyCarry.exe` 即可运行，无需安装。

## 技术栈

- WinUI 3 (Windows App SDK 2.0) + .NET 9
- C# 13, x64, unpackaged self-contained
- 外部工具: rclone.exe

## 构建

```powershell
# 需要 .NET 9 SDK + rclone.exe 放到 src/ComfyCarry/Assets/
dotnet build src/ComfyCarry/ComfyCarry.csproj -c Debug -p:Platform=x64

# 发布（CI 使用，两步）
dotnet publish src/ComfyCarry.Launcher/ComfyCarry.Launcher.csproj -c Release -p:Platform=x64 -r win-x64 --self-contained false /p:PublishSingleFile=true -o publish-launcher
dotnet publish src/ComfyCarry/ComfyCarry.csproj -c Release -p:Platform=x64 -r win-x64 --self-contained true /p:WindowsAppSdkSelfContained=true /p:PublishSingleFile=false /p:EnableMsixTooling=true -o publish-app
```

## 发版

更新 csproj 中的 `<Version>`，commit 后打 tag：

```bash
git tag v0.2.0
git push origin v0.2.0
```

CI 会自动构建并发布 GitHub Release。

## 数据目录

`%LOCALAPPDATA%\ComfyCarry\` - 实例连接、同步规则、设置、日志。凭据经 DPAPI 加密。
