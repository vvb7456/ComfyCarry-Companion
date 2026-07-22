# 产物取回模块重构 —— 实施计划（交本地 agent 执行）

> 面向 `/opt/ComfyCarry-Companion`（本仓库，WinUI 3 / .NET 8）。
> 本计划**只重构「产物取回」模块（Tab2 / PullPage 及其服务链）**，不动「云存储配置」（Tab1 向导，已验证）。
> UI 线稿：https://claude.ai/code/artifact/68dc07d6-0c95-471b-96bd-413ed3005446
> 面板侧对应线稿：https://claude.ai/code/artifact/f0fe15a9-0553-4e33-8462-26918cbd37c5

---

## 0. 为什么重构（根因）

现有产物取回模块建立在**旧契约「面板存规则」**之上，面板侧已重构、该契约作废：

1. `RuleEngine.RefreshAsync/SaveRuleAsync/DeleteRuleAsync` 走面板 `GET/POST/DELETE /api/companion/rules` —— **这三个端点已从面板删除（现在 404）**。
2. 规则存在面板 = 错的：`local_path` 是本机路径对面板无意义；面板所在 pod 易失，规则随实例销毁丢失。
3. 过滤器是**手写 rclone 表达式**（`FiltersBox`），用户体验差。
4. 心跳**不带 `rule_summaries`**，面板新版 Clients tab 的规则镜像会是空的。
5. Job event/finish 契约**已漂移**（详见 §2）。

**核心决策：规则归客户端所有、本地持久化、绑定 `instance_label`（跨实例重部署存活）；面板只收心跳里的只读摘要 + Job 汇报。**

---

## 1. 新的面板契约（客户端需对齐）

以下是面板当前实际实现（`comfycarry/routes/companion.py`），客户端按此对齐：

| 端点 | 方法 | 请求 | 响应 |
|---|---|---|---|
| `/api/companion/connect` | POST | `{password}` | `{ok, api_key, dav_url, dav_user, comfyui_dir, instance_label}` |
| `/api/companion/heartbeat` | POST | `{client_id, hostname, app_version, status, active_rule_id, progress, rule_summaries}` | `{ok, online, last_seen}` |
| `/api/companion/jobs` | POST | `{rule_id, rule_count?, client_id?}` | `{ok, job_id}` |
| `/api/companion/jobs/<job_id>/events` | POST | `{key, rule_id?, level?, params?}` | `{ok}` |
| `/api/companion/jobs/<job_id>/finish` | POST | `{status, success_count?, failure_count?, files_synced?, summary?}` | `{ok}` |

- **已删除（客户端必须停止调用）**：`GET /api/companion/rules`、`POST /api/companion/rules`、`DELETE /api/companion/rules/<id>`。
- `heartbeat.rule_summaries`：`list`，每项 `{name, source, local_path, method, trigger, last_result}`。面板原样存、Clients tab 只读展示，不校验内部结构。
- `jobs/<id>/finish.status` 取值 **必须** ∈ `success | failed | partial | cancelled`（不是 `ok/error`）。
- `jobs/<id>/events.key` 是事件键（字符串），面板据此渲染活动时间线；`params` 是可选 dict。
- 数据面：`dav_url`（形如 `https://<host>/dav`），basic auth = `dav_user`(comfy) + **面板密码**。客户端内置 rclone 连它。

---

## 2. 需修复的契约漂移（Models/ApiContracts.cs）

- **删除** `RulesResponse` 类（不再有 /rules）。
- `HeartbeatRequest`：新增 `RuleSummaries`：
  ```csharp
  [JsonPropertyName("rule_summaries")] public List<RuleSummary> RuleSummaries { get; set; } = new();
  // class RuleSummary { name, source, local_path, method, trigger, last_result }  全部 [JsonPropertyName] 小写下划线
  ```
- `JobEventRequest`：改为面板契约：
  ```csharp
  [JsonPropertyName("key")] public string Key;            // 事件键，如 "rule_start_pull"/"file_done"/"rule_error"
  [JsonPropertyName("rule_id")] public string? RuleId;
  [JsonPropertyName("level")] public string? Level;        // info|warning|error
  [JsonPropertyName("params")] public Dictionary<string,object>? Params;   // file/pct/speed/msg 塞这里
  ```
  （删除旧的 `type/file/pct/speed/message` 直接字段。）
- `JobFinishRequest`：改为：
  ```csharp
  [JsonPropertyName("status")] public string Status;       // success|failed|partial|cancelled
  [JsonPropertyName("success_count")] public int SuccessCount;
  [JsonPropertyName("failure_count")] public int FailureCount;
  [JsonPropertyName("files_synced")] public int FilesSynced;
  [JsonPropertyName("summary")] public string? Summary;
  ```
- `JobCreateRequest`：`{rule_id, client_id, rule_count}`（`trigger_type` 面板忽略，可删）。

---

## 3. 数据模型（Models/PullRule.cs）

规则本地化，去掉面板字段，加勾选式内容预设 + 本地运行状态：

```csharp
public sealed class PullRule {
  [JsonPropertyName("rule_id")]     public string RuleId = Guid.NewGuid().ToString("N");
  [JsonPropertyName("name")]        public string Name = "";
  [JsonPropertyName("source")]      public string Source = "";        // 实例 output 下相对子路径，如 "" 或 "video"
  [JsonPropertyName("local_path")]  public string LocalPath = "";     // Windows 路径
  [JsonPropertyName("method")]      public string Method = "copy";    // copy|move
  [JsonPropertyName("content")]     public string Content = "images"; // images|videos|all  ← 内容预设
  [JsonPropertyName("subdirs")]     public bool Subdirs = true;       // 含子目录
  [JsonPropertyName("trigger")]     public string Trigger = "watch";  // watch|manual
  [JsonPropertyName("enabled")]     public bool Enabled = true;
  [JsonPropertyName("last_result")] public string LastResult = "";    // 如 "22 文件 · 成功" / "失败: ..."
  [JsonPropertyName("last_run_at")] public DateTime? LastRunAt;
  [JsonIgnore] public string StatusText = "idle";
  [JsonIgnore] public int ProgressPct;
}
```

**删除**：`executor / client_id / direction / template_id / filters / interval_sec`。
- `filters` 由 `content + subdirs` 在拉取时**动态生成**（§6），不存原始表达式。
- `interval_sec` 改用全局设置 `settings.PullWatchIntervalSec`（所有 watch 规则共用一个检查周期），规则里不再单独存。

---

## 4. 规则存储（新增 Services/RuleStore.cs）

仿 `InstanceStore` 写一个本地规则存储：

- 文件：`data/rules.json`（`AppPaths` 加 `RulesFile = Path.Combine(DataDir,"rules.json")`）。
- 结构：`Dictionary<string, List<PullRule>>`，**key = instance_label**（面板 connect 返回的 `instance_label`；若为空回退用 `BaseUrl` 的 host）。这样规则绑逻辑实例、跨 api_key/重部署存活。
- API：`IReadOnlyList<PullRule> RulesFor(string label)`、`Upsert(label, rule)`、`Delete(label, ruleId)`、`Load()`、`Save()`、`event Changed`。
- 不加密（规则无敏感信息，路径而已）；`WriteIndented`。

`ServiceHub` 里 `new RuleStore(Paths)`，Load()，注入给 `RuleEngine`/`PullEngine`/`HeartbeatService`。

---

## 5. 服务链改造

### RuleEngine.cs（从「面板同步」改「本地状态 + 本地存储」）
- 删除 `_api` 依赖里对 rules 的调用；构造改注入 `RuleStore` + `InstanceStore`。
- `RefreshAsync(inst)` → 不再打面板，读 `RuleStore.RulesFor(inst.InstanceLabel)` 填充 `Rules`。
- `SaveRuleAsync(inst, rule)` → `RuleStore.Upsert(label, rule)` + 刷新 `Rules`（本地，无网络）。
- `DeleteRuleAsync(inst, ruleId)` → `RuleStore.Delete(label, ruleId)`。
- **删除** `Templates` 集合与 `NewFromTemplate`（模板改为客户端内置默认，见 §7 UI 的「新建」默认值即可）。
- 保留 `ActiveRule/Status/ProgressPct/ActiveFile/ActiveSpeed/SetActive/ReportProgress/MarkIdle/MarkError`（运行态，UI 绑定）。
- `DefaultFilters` 常量移到 §6 的过滤器构建器，或保留供 all-fallback。

### CompanionApiClient.cs
- **删除** `GetRulesAsync / SaveRuleAsync / DeleteRuleAsync`。
- 保留 `ConnectAsync / SendHeartbeatAsync / CreateJobAsync / AppendJobEventAsync / FinishJobAsync`，按 §2 新契约调整序列化。
- 401 自动重连逻辑保留（用面板密码重新 connect 刷 api_key）。

### HeartbeatService.cs
- 构造注入 `RuleStore`。`Tick` 里组装 `rule_summaries`：取当前实例 `RuleStore.RulesFor(inst.InstanceLabel)`，映射为 `RuleSummary{ name, source, local_path, method, trigger, last_result }`，放进 `HeartbeatRequest.RuleSummaries`。
- `status`/`active_rule_id`/`progress` 仍来自 `RuleEngine`。

### PullEngine.cs
- `Tick`：不再 `_rules.RefreshAsync(panel)`；直接读 `RuleStore.RulesFor(label)`，过滤 `Enabled && Trigger=="watch" && LocalPath!=""`，逐条 `RunOnceAsync`。
  - watch 语义 = 每个周期 rclone copy/move；rclone 只传新增/变化文件，天然实现「远端有更新即拉」。
- `RunOnceAsync`：
  - 拉取参数由 `RcloneService.PullAsync` 用 §6 的过滤器构建（读 `rule.Content/Subdirs`）。
  - Job：`CreateJobAsync({rule_id, client_id, rule_count:1})` → `AppendJobEventAsync(key:"rule_start_pull", params:{name})` → 进度事件 `key:"file_done", params:{file,pct,speed}` → 结束 `FinishJobAsync(status, files_synced, summary)`。status 映射：exit 0 → `success`，否则 `failed`，取消 → `cancelled`。
  - **拉取结束回写**：把结果写进 `rule.LastResult`（如 `"{n} 文件 · 成功"`）+ `rule.LastRunAt=now`，`RuleStore.Upsert(label, rule)`。这样 UI 卡片与心跳摘要都能显示「上次结果」。

### JobReporter.cs
- 按 §2 新的 event key / finish 字段调整封装方法签名。

---

## 6. 过滤器预设 → rclone 参数（放 RcloneService 或独立 FilterBuilder）

`PullAsync` 里根据 `rule.Content` / `rule.Subdirs` 生成参数（**替换**原来注入 `rule.Filters` 的逻辑）：

```
内容预设 → 扩展名集：
  images: png jpg jpeg webp gif bmp tiff tif
  videos: mp4 mov webm mkv avi
  all:    (不加类型过滤)

参数拼装：
  images/videos: 对每个扩展名 加 --include "*.<ext>"（大小写不敏感：rclone 默认区分，故补大写或用 --ignore-case）
                 用 rclone: 追加 --include "*.{png,jpg,...}"  （单条 brace 语法即可）
  all:           不加 --include
  subdirs=false: 追加 --max-depth 1
  subdirs=true:  默认递归，不加
```

建议实现：`List<string> BuildFilterArgs(PullRule r)` 返回参数列表，`PullAsync` 展开进命令。move 方式用 `rclone move`（拉后删源），copy 用 `rclone copy`。

---

## 7. UI 重建

### MainWindow.xaml —— 不动
保留 NavigationView 三项（云存储配置 / 产物取回 / 设置）。

### Views/PullPage.xaml(.cs) —— 按线稿重建
布局（自上而下）：
1. **标题区**：「产物取回」+ 一行副标题「把实例产物直接拉到本机，与网盘同步并行。」
2. **连接条**：实例下拉（`InstanceBox`）+ 连接状态点（已连接/未连接）+ dav host 简写 + 「重新连接」「管理实例」图标按钮。无实例 → 整页 `EmptyState` 大 CTA「连接实例」（保留现有空状态逻辑）。
3. **自动取回条**：`ToggleSwitch`（绑 `PullEngine.Paused` 反相）+ 说明「远端有新产物时自动拉到本机（每 Ns 检查）」+ 右侧当前总状态（空闲 / 拉取中·N 文件）。
4. **规则区**：标题「取回规则」+「＋ 新建规则」按钮；规则卡列表。
   - 规则卡：图标 + 名称 + method chip（复制/移动）+ trigger chip（自动/手动）+ 启用 ToggleSwitch；第二行 `source → local_path · 内容 · 含子目录`；激活时进度条；底部「上次 <time> · <last_result>」+ 操作按钮（立即拉取 / 编辑 / 打开文件夹 / 删除）。
   - 无规则 → 引导文案卡。
5. **移除**原来的「产物列表（WebDAV）」`Expander` + `ArtifactListView` 独立区块 —— 浏览能力并入规则编辑器的「来源」选择（§下）。

`.cs`：事件订阅 `RuleStore.Changed` + `RuleEngine.StateChanged`；`RunOnce/Edit/OpenFolder/Delete/NewRule` 走本地 RuleStore；move 规则「立即拉取」保留二次确认。

### Views/RuleEditDialog.xaml(.cs) —— 按线稿重建
字段：
- **名称** TextBox。
- **来源（实例目录）**：只读显示 + 「浏览…」按钮 → 弹出目录选择（用 `RcloneService.LsfAsync(inst, currentPath)` 列 output 下子目录，逐级进入选定一个），结果存 `rule.Source`。**不再有原始 filters 文本框**。
- **本机文件夹**：只读 + 「选择…」→ `FolderPicker`，存 `rule.LocalPath`。
- **内容**：`SegmentedControl`/三按钮（图片 / 视频 / 全部）→ `rule.Content`；旁边 CheckBox「含子目录」→ `rule.Subdirs`。
- **方式**：两段（复制（保留源）/ 移动（拉后删源））→ `rule.Method`；选 move 时显示 `InfoBar`/告警条。
- **触发**：两段（自动（watch）/ 手动）→ `rule.Trigger`。
- **启用** ToggleSwitch → `rule.Enabled`。
- 校验：名称、本机文件夹必填。保存 → `RuleEngine.SaveRuleAsync`（本地）。
- **移除**：原 filters 文本框、interval 数字框（interval 归全局设置）。

### 复用 ArtifactListView
改造为规则编辑器「来源浏览」的目录选择控件（或就地新建一个轻量目录选择弹窗）。不再作为主页常驻区块。

### 本地化（LocalizationService 资源）
- 更新/新增：`pull.autoPull`、`pull.autoPull.desc`、`pull.rule.source`、`pull.rule.content`（images/videos/all）、`pull.rule.subdirs`、`pull.rule.method.copy/move`、`pull.rule.trigger.auto/manual`、`pull.rule.lastResult`、`pull.connect.reconnect`、`pull.connect.manage`。
- 删除：`pull.rule.filters`、`pull.rule.interval`、模板相关键。
- 文案**精简短句**，中英对齐。

---

## 8. DI / 生命周期（ServiceHub.cs）

- `new RuleStore(Paths)` + `Load()`。
- `RuleEngine(RuleStore, InstanceStore, Jobs)`（去掉对 Api 的 rules 依赖）。
- `HeartbeatService(Api, Instances, Rules, RuleStore, Settings)`。
- `PullEngine(Rclone, Rules, RuleStore, Jobs, Instances, Paths, Settings, token)`。
- 连接成功后（ConnectDialog 保存实例时）确保 `EnsureClientId` + 存 `InstanceLabel`；`RuleStore` 以 label 归组。

---

## 9. 验收（本地 Windows 编译环境执行）

1. `dotnet build -c Release`（或 VS）—— 0 error。
2. 全局搜索确认**无残留**：`/api/companion/rules`、`GetRulesAsync`、`SaveRuleAsync`(面板版)、`DeleteRuleAsync`(面板版)、`Executor`、`TemplateId`、`RulesResponse`、`FiltersBox`、`IntervalBox`。
3. 冒烟（连真实例）：
   - 连接 → 新建规则（浏览来源目录、选本机夹、内容=图片、方式=复制、触发=自动）→ 保存 → `data/rules.json` 出现该规则。
   - 面板 Sync → 客户端 tab：该客户端卡片出现，规则镜像显示 `source → local_path` + copy/watch。
   - 自动/立即拉取 → 本机文件夹出现图片；面板「活动」tab 出现一条 companion 拉取 Job（状态 success）。
   - 关掉实例重建、重连（同 instance_label）→ 老规则仍在。
4. `vue`/前端无关（本模块纯客户端）。

---

## 10. 范围边界

- **不改** Tab1 云存储配置向导（已验证）。
- **不改** 面板侧（已完成，见面板线稿）。
- 公共 Tunnel 模式下 `/dav` ingress 未解决（面板侧 TODO），冒烟需用自定义 Tunnel 模式实例。
