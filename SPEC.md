# ComfyCarry Companion — Windows 桌面客户端 需求 & 实施文档 (2026-07-20)

> 背景: 当前持久化链路依赖 rclone 把实例的 `/workspace/ComfyUI` 双向同步到**第三方
> 网盘**。两个痛点: (1) 用户拿到 rclone.conf 的过程繁琐 —— 下 rclone.exe、丢到
> [rclone-setup.bat](../static/rclone-setup.bat) 同级、跑交互菜单 (内部用 PowerShell
> 正则抠 OAuth token、手动调 Graph 取 drive_id)、导出后再把内容粘进 Setup Wizard;
> (2) 想把生成产物取回本地, 必须绕第三方网盘一圈。
>
> 本文档定义一个独立的 **Windows 桌面客户端 (ComfyCarry Companion)**, 一并解决这两件事。
> **它不替换现有能力, 而是现有 rclone 规则系统的自然延伸**: pod↔网盘 的模型/产物同步照旧,
> 产物拉取到本机被建模成规则系统里新增的一类规则, 与网盘同步共用同一套规则/模板/Job/历史。
>
> 交付: 面板侧后端 + Sync 页扩展在本仓库实现; Windows 客户端由**独立 subagent** 从零构建
> (无本仓库上下文), 故本文档 API 契约自包含、可直接照做。项目**尚未发布, 不考虑向前兼容**,
> 一切按"当前最佳实践"设计, 不背历史包袱。

---

## 0. 架构决策

### 0.1 两个 Tab 分属两个生命周期阶段

| | Tab 1 · 云存储配置助手 | Tab 2 · 产物取回 |
|---|---|---|
| 阶段 | 部署**前** (实例还没起, 连不上) | 部署**后** (实例在跑, 可连) |
| 是否需连实例 | **否, 纯本地** | 是 |
| 产出/动作 | 生成 rclone.conf, 用户上传到 Wizard | 按规则把实例产物拉到本机 |
| 是否用 rclone | 是 (OAuth/建目录树) | 是 (执行拉取规则) |

关键点 (对应反馈 9): **Tab 1 不把配置推给实例**。因为 rclone 绝大多数在 Wizard 阶段配置,
那时实例尚未连接。所以沿用现有做法 —— Tab 1 **本地生成 rclone.conf, 用户自行上传到 Wizard**
的"云存储"步骤 (该步已支持文件上传)。Tab 1 相对旧 bat 的价值 = 真正的 Fluent GUI + 重构后
稳健的 OAuth + 仍然产出标准 rclone.conf。

### 0.2 产物拉取 = 现有 rclone 规则系统的扩展, 不是独立功能 (对应反馈 5/6/10)

产物取回**不**自造一套 HTTP 增量算法, 而是复用现有规则引擎的全部概念:

- 客户端**内置并运行 rclone** 执行拉取 —— 与 Tab 1 共用同一个 rclone.exe。
- 面板侧暴露一个 **rclone 兼容的只读后端** (WebDAV over 现有 CF Tunnel, 见 §0.3)。
- 拉取的"什么/方向/方法/过滤/触发"全部由**规则**驱动, 规则形状与
  [config.py](../comfycarry/config.py) 的 `SYNC_RULE_TEMPLATES` / sync_rules **完全一致**:
  `direction=pull`、`method=copy|move|sync`、`filters=[...]`、`trigger=watch|manual`、
  `local_path`(本机文件夹)、`remote_path`(实例内子目录)。
- **不硬编码产物是什么**: 预设默认规则模板 (拉图片+视频到本机), 用户可改过滤器、加规则。
- **保留还是删除实例文件**: 由规则 `method` 决定 (`copy` 留、`move` 拉后删), 与现有网盘
  同步模板同源, 不写死在代码里。
- Job / 事件 / 历史复用 sync_store, 在面板 Sync 页统一展示 (见 §2.5)。

一句话: **Companion 的拉取能力是把"规则的执行端"从 pod 扩展到了 PC**。规则还是那套规则。

### 0.3 传输层: 面板 rclone serve WebDAV, 经现有 CF Tunnel 暴露

pod 上跑 `rclone serve webdav`, 绑定 `localhost:<port>`, `--baseurl /dav`; cloudflared
在**已有的那条 tunnel** 上加一条 **路径 ingress** (`^/dav/` → `localhost:<port>`), 其余路径
仍走 Flask。客户端的 WebDAV 地址就是 `https://<用户开面板的同一域名>/dav`。

- 无需第二个子域名、无需知道实例公网 IP/端口 —— 复用面板已管理的 tunnel。
- 内容根 = 面板侧设置项, **默认只暴露 `output/`** (安全, 不外泄模型); 用户可加 (如
  `workflow/`)。暴露的根 + 规则一起决定拉什么。
- 鉴权: `rclone serve webdav --user comfy --pass <面板密码>` (basic auth), 客户端用**同一个
  面板密码**连接 (对应反馈 2, 单一凭据)。

**Cloudflare 限制应对** (只影响该传输方向):
- CF 边缘 100 MB 只卡**上传请求体**; 拉取是下载, 不受限。
- CF 100s 源站响应超时 → 单个大文件 (视频) 一次 GET 可能超时。用 rclone 的**多线程分块下载**
  规避: `--multi-thread-cutoff 32M --multi-thread-streams 4`, 每块是独立的 Range 请求、秒级
  完成, 不会撞 100s。WebDAV 后端支持 Range, 天然可分块。

### 0.4 连接方式: 只用 面板 URL + 面板密码 (对应反馈 2/3/4)

- Companion 连接一个实例只需 **面板 URL + 面板登录密码** —— 用户本就知道的东西。
- 客户端用密码换取 API Key (见 §2.2), 自动存起来, 供后续 JSON API 调用; 用户**不必自己去
  UI 深处翻 API Key**。
- **不做配对串/二维码** (反馈 3), **不做面板能力自检端点** (反馈 4, 未发布无需向前兼容)。
- 注意区分: §2.4 的客户端→面板**心跳**不是"能力自检", 是为了让 Sync 页展示客户端在线状态与
  运行中的规则 (反馈 7 明确要), 保留。

```
┌──────────────────────── ComfyCarry Companion (单 .exe, 内置 rclone.exe) ────────────────┐
│                                                                                          │
│  Tab 1 · 云配置 (部署前, 纯本地)              Tab 2 · 产物取回 (部署后, 连实例)            │
│  ┌───────────────────────────┐               ┌──────────────────────────────┐           │
│  │ Fluent 向导配 remote      │               │ URL + 面板密码 连接           │           │
│  │ 重构后的稳健 OAuth        │               │ 拉取规则(与网盘规则同源)      │           │
│  │ (rclone 自己写 conf)      │               │ 内置 rclone 执行 pull         │           │
│  │ 建标准目录树              │               │ 回报 Job → 面板 Sync 页       │           │
│  │ ⇒ 输出 rclone.conf 文件   │               └───────────────┬──────────────┘           │
│  └───────────────────────────┘                               │                          │
└────────────┬─────────────────────────────────────────────────┼──────────────────────────┘
             │ 用户手动上传                        rclone(WebDAV over /dav) + JSON API
             ▼                                                   ▼      (面板密码 / X-API-Key)
    ┌──────────────────┐                          ┌──────────────────────────────────┐
    │ Setup Wizard     │                          │ 面板 @ CF Tunnel                 │
    │ 云存储步骤(文件上传)                          │ Flask API + rclone serve /dav    │
    └──────────────────┘                          │ + Sync 页(客户端状态/规则/历史)  │
                                                   └──────────────────────────────────┘
```

---

## 1. 连接与鉴权 (面板侧)

### 1.1 现状 (无需改动)

面板 [auth.py](../comfycarry/auth.py) 中间件已支持 API Key 鉴权 (`X-API-Key` 或
`Authorization: Bearer`), 匹配 `config.API_KEY` 即放行, 绕开 session cookie —— 无头客户端
理想选择。`API_KEY` 自动生成 (`cc-<48hex>`), 存 `.dashboard_env`。

### 1.2 客户端用密码换 API Key

见 §2.2 新端点 `POST /api/companion/connect`。客户端只让用户填 URL + 面板密码, 背后自动完成
换取与存储。

### 1.3 rclone serve 的鉴权

`rclone serve webdav` 用 basic auth, 凭据 = 面板密码 (§0.3)。客户端 rclone 侧用
`--user comfy --pass <面板密码>` (或配到本地 remote 的 `pass`, obscure 存储)。

---

## 2. 面板侧后端改动 (本仓库实现)

新蓝图 `comfycarry/routes/companion.py`。JSON 端点走既有中间件鉴权 (`X-API-Key`/`Bearer`),
错误统一 `{"error": "..."}` + HTTP 状态码。

### 2.1 rclone serve WebDAV 后端 + tunnel 路由

- 面板管理一个常驻 `rclone serve webdav` 进程 (随 dashboard 生命周期起停, 建议纳入现有
  pm2/服务管理):
  - `rclone serve webdav <content_root> --addr 127.0.0.1:<port> --baseurl /dav --user comfy --pass <DASHBOARD_PASSWORD> --read-only`
  - `<content_root>` = 面板设置项 `companion_serve_roots` 的落地目录; 默认
    `COMFYUI_DIR/output`。若允许多根, 用一个聚合目录 (symlink 或 `--dir-cache` 组合) 或起多个
    baseurl。**一期只暴露 `output/`, 单根。**
  - `--read-only`: serve 端只读, 删除动作由客户端 rclone 的 `move` 语义在**本地完成后**回删
    源 —— 见下方说明。
- cloudflared ingress 增加一条**路径规则**: `path: ^/dav/` → `http://127.0.0.1:<port>`,
  置于 catch-all(→Flask) 之前。纳入现有 tunnel 配置管理 (面板已有多服务 DNS/隧道管理)。
- **关于 move (拉后删)**: `--read-only` 会阻止 WebDAV 侧删除。若规则 `method=move` 需要删除
  实例源文件, 有两种落地, 一期选**方案 A**:
  - **方案 A (一期)**: serve 不开 `--read-only`; rclone `move` 拉完自动删源。删除范围受 serve
    content_root 限制 (仅 `output/`), 风险可控。
  - 方案 B (备选): serve 只读 + 客户端拉完调 `POST /api/companion/purge` 让面板删已确认文件。
    更可控但多一环, 列入待决。
  → 采用方案 A。

### 2.2 客户端连接 (密码 → API Key) — `POST /api/companion/connect`

**此端点需对未登录放行** (在 auth 中间件加入放行名单, 类似现有 setup 放行), 因为客户端此时
还没有任何凭据, 仅持密码。

**请求**: `{ "password": "<面板密码>" }`

**行为**: 比对 `config.DASHBOARD_PASSWORD`。不符 → 401。符合 → 返回连接所需信息。

**响应**:
```json
{
  "ok": true,
  "api_key": "cc-...",                 // 供后续 JSON API
  "dav_url": "https://comfy.example.com/dav",  // 由 request host 推断
  "dav_user": "comfy",
  "comfyui_dir": "/workspace/ComfyUI",
  "instance_label": "runpod-a1b2"      // 面板已配置的实例名, 无则回空
}
```
客户端存: 面板密码 (WebDAV basic auth 用) + api_key (JSON API 用), 均 DPAPI 加密 (§3.5)。
后续任一 401 → 用密码重新 `connect` 刷新。

### 2.3 拉取规则纳入现有规则系统 — 规则 CRUD

拉取规则与网盘规则共用 sync 规则存储, 新增一个维度区分执行端:

- sync_rules 每条规则增加字段:
  - `executor`: `"pod"` (默认, 现有 pod 内执行) | `"companion"` (客户端执行)。
  - `client_id`: `executor=companion` 时归属的客户端 id (支持多客户端各自的规则)。
- pod 的 sync worker ([sync_engine.py](../comfycarry/services/sync_engine.py) `_sync_worker_loop`)
  **只跑 `executor=pod` 的规则**, 忽略 companion 规则。
- companion 规则的 `local_path` 是 **Windows 路径** (如 `D:\ComfyCarry\out\`), 面板只存储与
  展示, 不解释。`remote_path` 为 serve content_root 下的相对子路径 (默认 `""` = output 根)。

端点 (均需鉴权):
- `GET  /api/companion/rules?client_id=<id>` → 该客户端的规则列表 (+ 可返回预设模板供客户端渲染)。
- `POST /api/companion/rules` → 新建/更新规则 (body 为规则对象, 校验字段)。
- `DELETE /api/companion/rules/<rule_id>`。
- 复用现有 sync 规则模板机制: 新增 `direction=pull` 且 `executor=companion` 的预设模板 (见 §4.3)。

> 也可让客户端本地维护规则、仅上报; 但**以面板为规则唯一真源**更契合"规则系统的扩展", 且让
> Sync 页无需客户端在线也能看到规则。采用面板为真源。

### 2.4 客户端注册 + 心跳 — `POST /api/companion/heartbeat`

供 Sync 页展示客户端在线状态与当前活动 (反馈 7)。**这不是能力自检**, 是状态上报。

**请求**:
```json
{
  "client_id": "<客户端首次连接时生成的稳定uuid>",
  "hostname": "DESKTOP-XX",
  "app_version": "1.0.0",
  "status": "idle|syncing|paused|error",
  "active_rule_id": "rule-...",         // 正在跑的规则, 无则空
  "progress": { "file": "...", "pct": 42, "speed": 1048576 }
}
```
面板记录 `last_seen`、状态, 供 §2.5 展示。心跳周期建议 15~30s。面板据 `last_seen` 超时
判定离线。

### 2.5 Job 回报 (复用 sync_store)

客户端执行拉取规则时, 把 Job/事件回报面板, 复用现有 sync_store 的 job/event 表, 使 Sync 页
历史统一:
- `POST /api/companion/jobs` → 创建 job (映射 sync_store.create_job, `trigger_type="companion"`)。
- `POST /api/companion/jobs/<job_id>/events` → 追加事件 (rule_start/file_transferred/rule_done…)。
- `POST /api/companion/jobs/<job_id>/finish` → 收尾 (status/统计, 映射 finish_job)。

字段与 [sync_engine.py](../comfycarry/services/sync_engine.py) 现有 job/event 结构对齐, 便于
Sync 页复用同一渲染。

### 2.6 前端: 扩展 Sync 页 (反馈 7)

在现有 [SyncPage.vue](../frontend/src/pages/SyncPage.vue) 增加"客户端 (Companion)"区块, 风格
参考页面现有卡片:
- **客户端连接状态**: 在线/离线 (据 heartbeat last_seen)、hostname、app 版本、当前状态。
- **运行中的规则**: 列出 `executor=companion` 的规则 (方向/方法/过滤/本机路径/所属客户端), 当前
  活动规则高亮 + 进度。
- **统一 Job 历史**: companion 的 job 与 pod 的 job 同列展示 (可加来源标签 pod/客户端)。
- 规则可在此页查看; 编辑主要在客户端做 (本机路径只有客户端知道), 面板侧至少支持启用/停用/删除。

新增读接口按需: `GET /api/companion/clients` (在线客户端列表 + 状态)。

---

## 3. Windows 客户端 (独立 subagent 构建)

> 本节自包含。subagent 仅凭本文档 §1–§4 即可实现完整客户端。

### 3.1 技术栈

**WinUI 3 (Windows App SDK) + C#/.NET 8, 非打包 (unpackaged) 自包含发布。**

- WinUI 3 = 微软官方桌面栈, 原生 Fluent 2 Design, 满足"官方 UI 准则"。
- 非打包 + 自包含 → 双击即用的 `.exe` (随附 runtime, 用户无需装 .NET/MSIX)。
- 内置 rclone.exe (随包), Tab 1/Tab 2 共用。
- 目标 Win10 1809+ / Win11; 明暗跟随系统; 中英双语 (简中为主); 单实例 + 系统托盘常驻
  (关窗最小化到托盘, 后台继续按规则拉取)。
- 不用 Electron。

### 3.2 信息架构

```
启动 → 主窗口 (NavigationView 左侧导航)
  ├─ 云存储配置   → Tab 1 (§3.3)  ── 不需连实例
  ├─ 产物取回     → Tab 2 (§3.4)  ── 需连实例
  ├─ 设置         → 实例(连接)管理 / 语言 / 主题 / 开机自启 / 关于
  └─ 托盘: 显示主窗口 / 拉取状态(idle·syncing·N待取) / 暂停·继续 / 退出
```

### 3.3 Tab 1 · 云存储配置助手 (纯本地, 产出 rclone.conf)

流程 (Fluent 分步向导):

1. **选云类型**: OneDrive(个人/商业) / Google Drive / Dropbox / WebDAV(坚果云·群晖·AList) /
   Cloudflare R2 / AWS S3 (与旧 bat 覆盖一致, 表单字段可参照面板 `REMOTE_TYPE_DEFS`)。
2. **(可选) 代理**: 输入 `http://127.0.0.1:7890`, 作为 rclone 子进程的
   `HTTP_PROXY`/`HTTPS_PROXY` 环境变量 (GDrive/Dropbox 常需)。
3. **授权 / 填参 — 重构后的 OAuth (对应反馈 8)**:

   旧 bat 的问题: `rclone authorize` + **PowerShell 正则抠 token** + 手动调 Graph 取 drive_id,
   脆弱且易错。**弃用之。** 新做法的核心原则: **让 rclone 自己完成 OAuth 并自己写配置, 我们
   只读回它写的 conf, 绝不手动解析 token。**

   具体: 用 rclone 的**非交互式配置状态机** (`rclone config create <name> <type> <k=v>...
   --non-interactive`, 写到 app 私有的 `--config <temp.conf>`)。rclone 返回 JSON 状态对象:
   - 需要 OAuth 时, 状态里给出授权信息; 因为客户端与浏览器同机, 直接让 rclone 走本机
     `127.0.0.1:53682` 回调完成授权 (调 `rclone authorize` 子命令或继续状态机, rclone 自动开
     浏览器)。
   - 需要后续选择 (如 **OneDrive 选 drive / drive_type**) 时, 状态机返回可选项, GUI 用
     Fluent 控件呈现, 把用户选择通过 `rclone config update <name> --continue --state <s>
     --result <r>` 回灌 —— **drive_id 由 rclone 原生流程得到, 不再手写 Graph 调用**。
   - WebDAV/R2/S3 无 OAuth, 直接 `config create` 填 `url/user/pass` 或 `access_key_id/…`。
   - remote 命名校验 `^[a-zA-Z0-9_-]+$` (与面板一致)。

   结果: 全部配置由 rclone 写进那个临时 conf, 客户端**读回文件**即得标准 rclone.conf 文本。

4. **连接测试**: `rclone lsd <remote>: --config <temp.conf>`, 展示成功/失败。
5. **建标准目录树** (可选): `rclone mkdir` 创建 §4.4 结构。
6. **导出 rclone.conf**: 客户端可累积多个 remote 到同一 conf; 完成后**保存为
   `rclone.conf` 文件** (并提供"复制内容"). 引导文案: *"打开面板 Setup Wizard 的『云存储』步骤,
   上传此 rclone.conf 文件。"* —— **不连实例、不推送** (对应反馈 9)。

敏感 token 若需在本机暂存, 用 DPAPI 加密。

### 3.4 Tab 2 · 产物取回 (规则驱动的 rclone 拉取)

**连接**: 用户填 面板 URL + 面板密码 → 客户端调 `POST /api/companion/connect` (§2.2) 取回
`api_key` + `dav_url` + `dav_user` → 存储 (§3.5)。多实例: 列表管理 + 顶部切换。

**本地 rclone remote**: 客户端为该实例生成一个 rclone remote (webdav 类型), 指向 `dav_url`,
`user=dav_user`, `pass=面板密码` (obscure), 存于 app 私有 conf。

**规则 (与网盘规则同源, 反馈 5/6/10)**:
- 客户端从 `GET /api/companion/rules` 拉取本客户端规则 + 预设模板 (§4.3)。
- UI 让用户: 选模板 / 新建规则 —— 设 本机目标文件夹(`local_path`)、`method`(copy 留 / move
  拉后删)、`filters`(默认图片+视频, 可改)、`trigger`(watch 定时 / manual)、间隔。新建/改动
  `POST /api/companion/rules` 存回面板 (面板为真源)。
- **不硬编码产物类型**: 过滤器即产物定义, 用户可自定义 (拉 workflow、只拉某扩展名等)。

**执行 (调 rclone, 不自造算法)**:
- watch 规则: 客户端后台按 `trigger`/间隔调 rclone 执行:
  `rclone <method> <remote>:<remote_path> <local_path> --filter ... --multi-thread-cutoff 32M --multi-thread-streams 4 --use-json-log --stats-one-line`
- rclone 自带增量 (size/modtime 比对)、断点续传、Range 分块 —— **CF 100s 超时由多线程分块规避**
  (§0.3)。
- 客户端解析 rclone JSON 日志, 通过 §2.5 端点把 Job/事件回报面板 → Sync 页统一展示。
- 心跳 (§2.4) 周期上报在线状态 + 当前进度。
- UI: 产物网格/列表 (缩略图可经 WebDAV 取)、每规则状态、总进度、"打开本地文件夹"。文案强调
  *"这是把产物额外拉一份到本机, 与你在面板配置的网盘同步互不影响、可并存。"*

### 3.5 凭据与状态存储

- app 数据目录 `%LOCALAPPDATA%\ComfyCarry\`: instances.json、各实例 rclone remote conf、
  规则本地缓存、rclone.exe。
- **凭据 (面板密码、api_key、WebDAV pass)** 用 Windows **DPAPI** (`ProtectedData`, CurrentUser)
  加密存储, 不落明文。
- `client_id` 首次生成稳定 uuid, 用于面板侧规则归属与心跳。

### 3.6 错误与降级

- JSON API / connect 返回 401 → 密码失效, 引导重连。
- WebDAV 401 → 面板密码变更, 引导重连刷新。
- 网络/CF 抖动 → rclone 自带重试 + 客户端退避; 托盘标记, 不弹窗轰炸。
- 面板 Setup 未完成 (503 `setup_required`) → 提示"实例尚未完成部署"。

---

## 4. 附录: 面板既有约定

### 4.1 相关既有机制

| 项 | 位置 |
|---|---|
| API Key 鉴权中间件 | [auth.py](../comfycarry/auth.py) `check_auth` |
| API Key 生成/暴露/轮换 | [config.py](../comfycarry/config.py) `_load_api_key`; `/api/settings`, `/api/settings/api_key` |
| 面板密码 | `config.DASHBOARD_PASSWORD` |
| rclone.conf 路径 (实例侧) | `config.RCLONE_CONF` = `~/.config/rclone/rclone.conf` |
| 同步规则引擎 / worker / Job | [sync_engine.py](../comfycarry/services/sync_engine.py); sync_store |
| 规则模板 / 云类型字段定义 | [config.py](../comfycarry/config.py) `SYNC_RULE_TEMPLATES`, `REMOTE_TYPE_DEFS` |
| Sync 页 (待扩展) | [SyncPage.vue](../frontend/src/pages/SyncPage.vue) |
| 产物目录 | `COMFYUI_DIR/output` |
| 镜像内 Windows rclone.exe | `/opt/rclone-win/rclone.exe`; `GET /api/setup/rclone-bundle` |

### 4.2 支持的云类型 (Tab 1)

OneDrive(个人/商业) · Google Drive · Dropbox · WebDAV · Cloudflare R2 (s3/provider=Cloudflare) ·
AWS S3 (s3/provider=AWS)。

### 4.3 预设规则模板 (拉取, companion 执行)

参照现有 `SYNC_RULE_TEMPLATES` 形状, 新增 `direction=pull` / `executor=companion` 模板, 例如:

```
拉取产物到本机 (保留实例):  method=copy  remote_path="" (output根)  filters=[图片+视频]  trigger=watch
拉取产物到本机 (拉后清空):  method=move  remote_path=""             filters=[图片+视频]  trigger=watch
```

过滤器默认集与 §4.4 对齐, **用户可自定义** (改扩展名、加 workflow 等)。

### 4.4 默认产物扩展名集 (与现有产物同步模板 filter 对齐)

`png jpg jpeg webp gif bmp tiff tif mp4 mov webm mkv avi`

---

## 5. 分期交付

### 一期 (MVP)
**面板**: `companion.py` 蓝图 —— `connect` (密码换 key) + `rules` CRUD (含 executor/client_id
字段迁移) + `heartbeat` + `jobs` 回报; 常驻 `rclone serve webdav` (只暴露 output/) + tunnel
`/dav` 路径 ingress; Sync 页加客户端状态/规则/统一历史区块。
**客户端**: Tab 1 (重构 OAuth, 产出 rclone.conf 供 Wizard 上传) + Tab 2 (URL+密码连接 → 规则
驱动 rclone 拉取 → 回报) + 托盘常驻 + DPAPI 凭据。
→ 用户不再碰旧 bat/手抠 token; 产物按规则直取本机, 且在 Sync 页统一可见。

### 二期 (打磨)
- 多内容根 (拉 workflow 等) + 服务端缩略图端点省流量。
- move 的方案 B (serve 只读 + purge 确认删)。
- 多实例聚合视图、拉取统计、客户端自动更新。

### 5.1 分发
- 客户端: 单 `.exe` (自包含), 内置 rclone.exe。
- 面板设置页/Sync 页提供下载入口; 也可放 GitHub Release。

---

## 6. 安全考量

- 面板密码 / API Key 等价完全访问权 → DPAPI 加密存储, 传输仅经 HTTPS(CF Tunnel)。
- `rclone serve` **内容根严格限定** (默认仅 `output/`), 不暴露模型/系统目录; basic auth 用
  面板密码。
- `move` 规则会删实例源文件, 但删除范围受 serve content_root 约束 (仅 output/); UI 对 move
  模板二次确认。
- `connect` 端点在中间件放行但仅凭密码返回凭据, 需防暴力破解 (建议加失败次数限制/延迟)。

---

## 7. 开放问题 / 待决

1. **多内容根**: 一期只 `output/`; 是否二期支持用户勾选额外暴露目录 (workflow/input)?
2. **move 落地**: 一期方案 A (serve 可写, rclone move 删源); 是否需二期方案 B (只读 + purge)?
3. **多客户端同规则**: 不同 PC 连同一实例, 规则按 client_id 隔离; 是否需要"共享规则"概念?
4. **Sync 页规则编辑**: companion 规则 local_path 只有客户端知道, 面板侧编辑到什么粒度 (倾向
   面板只做启停/删, 编辑在客户端)?
5. **rclone serve 生命周期**: 随 dashboard 起停, 用 pm2 还是 Flask 子进程托管? (倾向 pm2, 与
   现有服务一致)
```
