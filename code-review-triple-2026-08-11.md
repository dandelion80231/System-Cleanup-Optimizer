# 代码审查报告（三遍）— CpqSystemTool v1.02

审查日期：2026-08-11
审查对象：v1.02 全量源码（46 个 .cs，约 3.2 MB，含 `MainWindow.Pages.cs` 306 KB 巨型分部类）
方法：code-review 技能思想 + 三视角并行子 Agent（逻辑/控制流、冗余/死代码、安全/健壮性），全部只读不写。
说明：WebView2 `SetupCdp` 同步死锁、PowerShell `-EncodedCommand` 统一化二项已在 v1.02 落地，不在本报告重复。

---

## 第一遍 · 逻辑 / 控制流 Bug

> 维度：空引用、资源泄漏、死锁/竞态、异常吞掉、逻辑错误、类型/转换。

| 严重度 | 位置 | 类别 | 问题 | 建议 |
|---|---|---|---|---|
| Med | `MainWindow.Probe.cs:410` | 空引用 | `links.Find(l => IsAppStoreUrl(...))` 无命中时返回 `null`，随后 `storeHit.Href` 抛 NRE；异常被外层 `catch` 吞掉并置 `NotFound=true`，导致"仅商店分发"的结果被误报为"未找到官网"。 | `if (storeHit != null && !string.IsNullOrEmpty(storeHit.Href)) { res.StoreOnly=true; ... } else res.NotFound=true;` |
| Med | `Modules/ProbeBrowserHost.cs:436` | 潜在挂起 | `BeginInvoke(new Action(async () => { tcs.TrySetResult(await func()); }))` 中，若 `func()` 在**首条 await 之前**同步抛异常（如 `_core` 未就绪），异常不进 `tcs`，调用方 `Probe.cs:336` 的 `.GetAwaiter().GetResult()` 将**永久阻塞挂起**。正常流程首 await 前不易抛，但属真实控制流缺陷。 | 把 async lambda 包装为显式 `Task`，确保同步异常也 `tcs.TrySetException(ex)`。 |
| Low | `Modules/ChocolateyResolver.cs:109,114` `Modules/SoftwareInstall.cs:554,561,1262,1277` | 阻塞 | 同步上下文中的 `.Result`/`.GetAwaiter().GetResult()`。均位于**非 async** 方法，无 captured SynchronizationContext，本身不死锁；但在 UI 线程调用会冻结最长 25s（Chocolatey）或整段下载期间。 | 上层改 async/await，避免同步阻塞。 |
| Low | `MainWindow.Maint.cs:694` | 异常吞掉 | `RefreshDepStatus` 中 `IsNodeDepsReady(probesDir).Ready` 异常被空 `catch {}` 吞掉，`nodeReady` 保持 false，把"检测异常"误显示为"Node 未安装"。 | 至少 `Debug.WriteLine` 记录，或把 `nodeReady` 单独置为"检测失败"状态。 |

**本轴最坏项**：`MainWindow.Probe.cs:410` 的空引用误报（Med，会影响探针结果正确性）。

**已检查且无明显 bug 的模块**：ProbeEngine.cs、Activation.cs、RestorePoint.cs、EdgeCore.cs、Tweaks.cs、OtherTweaksDialog.cs（Dispatcher 封送正确）、Helpers/Exec.cs（stdout/stderr 均 ReadToEnd 无死锁）、Helpers/RegistryHelper.cs、MainWindow.Theme.cs、AppxManager.cs、Updater/Cleanup/ServiceOptimizer/FirewallCore/PrivacyCore（Parallel.ForEach 均经 Dispatcher 回 UI 且捕获良好）。

---

## 第二遍 · 冗余与死代码

> 维度：未使用成员、跨文件重复代码、注释残留、可简化结构。

### 死代码 / 调试输出
- `MainWindow.Theme.cs:385` — 裸 `Debug.WriteLine` 状态跟踪（不在 catch 内），生产代码调试噪声，建议删除或改条件日志。
- `OtherTweaksDialog.cs:236-239` — 孤立 `/// <summary>` 文档注释（方法已迁至 `DialogBtnFx.RoundedTemplate`，注释后无方法体），建议删除。
- `RegistryHelper.cs:8-9` — 类摘要同一句重复两次，建议删一句。
- `CustomSoftwareDialog.cs:304` — `ColumnDefinitions[1].Width` 重复赋与上一行同值，建议删该行。

### 重复代码（维护成本，建议提取）
- `InstallPathDialog.cs:55-87` — 内联整段按钮 `ControlTemplate`/触发器，与 `DialogChrome.Apply`（`CustomSoftwareDialog.cs:21-61`）逐字相同但未复用 → 改调 `DialogChrome.Apply(this, owner)`。
- `CustomSoftwareEditDialog.cs:508-514` 与 `InstallPathDialog.cs:313-319` — `ShowError` 淡入错误提示逻辑完全一致 → 提至 `DialogChrome` 共享。
- 标题栏/关闭X/拖拽脚手架 — 4 份近同实现：`CustomSoftwareEditDialog.cs:110-149`、`CustomSoftwareManagerDialog.cs:562-600`、`InstallPathDialog.cs:107-154`、`Tier3ConfirmDialog.cs:51-94` → 提取 `DialogChrome.BuildTitleBar(...)`。
- `OtherTweaksDialog.cs:190-196` — 内联 `taskkill explorer` + `Process.Start("explorer.exe")` 与 `RegistryHelper.RestartExplorer` 重复 → 改调该方法。
- `Helpers/Exec.cs:91,218,222,254,277,281,298` — 7 处相同超时 Kill 模板 → 提取 `Exec.KillIfTimeout(p)`。
- `OtherTweaksDialog.cs` / `Tweaks.cs` 大量 `try{using(k=OpenSubKey) is int v&&v==1}catch{return false}` → 加 `RegistryHelper.GetDwordState(hive,path,name,onVal)` 辅助。

### 可简化
- `Tier3ConfirmDialog.cs:267` — `Enumerable.Range(0,_all.Count).Where(i=>_boxes[i].IsChecked==true).Sum(i=>_all[i].SizeMB)` 可简化为 `_boxes.Zip(_all,...)` 配对遍历。
- `MainWindow.Theme.cs:150` — catch 内 pwsh7 通道失败 `Debug.WriteLine`，与 :385 一并收敛日志量。

**本轴最坏项**：4 份重复的标题栏脚手架 + `InstallPathDialog` 未复用 `DialogChrome`——改动量小但重复面最大，是后续回归的主要风险点。

**已确认干净**：Exec.cs（Kill 模板除外封装良好）、RegistryHelper.cs（成员均被引用）、CustomSoftwareDialog.cs（DialogChrome 复用良好）、Tier3ConfirmDialog.cs、App.xaml.cs、Probe*、Updater.cs、VersionSwitch.cs、FirewallCore.cs。

---

## 第三遍 · 安全 / 健壮性与边界

> 维度：PowerShell 注入、路径/文件、网络完整性、输入校验、文化敏感、边界。

### 安全
| 严重度 | 位置 | 类别 | 问题 | 建议 |
|---|---|---|---|---|
| Med | `Modules/Activation.cs:254-264` | PowerShell/信任 | MAS 脚本从 `get.activated.win` 联网下载即用，仅依赖 HTTPS，未做哈希/签名钉扎；且用裸 `powershell.exe` 而非 `Exec` 的 SystemDirectory 完整路径，PATH 污染可劫持。 | 复用 `Exec.RunPS` 同款完整路径 + `-EncodedCommand`，对脚本内容加哈希钉扎。 |
| Low/Med | `Modules/EdgeCore.cs:99-101` | 网络完整性 | `RepairWebView2` 经 Invoke-WebRequest 下载引导程序到桌面后即 `/silent /install`，无 Authenticode/哈希校验。 | 校验签名或文件哈希后再安装。 |
| Low/Med | `Modules/OfficeInstall.cs:83-86` | 网络完整性 | ODT `setup.exe` 用 WebClient 下载，仅按大小 >100000 判定，无哈希/签名校验。 | 加哈希/签名校验。 |
| Low | `Modules/ChocolateyResolver.cs:109` | 输入校验 | `id` 直接拼入 OData 过滤串 `Id eq '`+id+`'`，含单引号可破坏查询。 | 对 `id` 做 `[A-Za-z0-9.\-]` 白名单。 |
| Low | `Modules/OfficeInstall.cs:115-116` | 输入校验 | `pid/channel` 直接拼入 XML，用户值含尖括号/引号会破坏结构。 | 用 `XmlTextWriter`/转义。 |

### 健壮性
- `MainWindow.Pages.cs:1836-1839` Low — `CheckForUpdate` 用 `WebClient.DownloadString` 无超时（默认无限），异常网络会长期挂起（后台线程不致冻 UI）；建议设 `Timeout` 或改 HttpClient。
- `Helpers/RegistryHelper.cs:165` Low — `RunCommand` 超时仅 `WaitForExit` 不 `Kill`，进程可能残留；`Exec` 的 RunCmd/RunPS 已统一 Kill，建议此处一致。

### 边界
- `MainWindow.Pages.cs:2231/2236/4046/4058/4063`、`SoftwareInstall.cs:204`、`Tweaks.cs:857` Low — 多处 `ToLower()`/`ToUpper()` 未用 `Ordinal`/`InvariantCulture`（搜索过滤、扩展名判断）；土耳其语等区域 `I/i` 映射不同会误判，建议 `ToLowerInvariant()`/`StringComparison.Ordinal*`。
- `Modules/ProbeEngine.cs:284` Low — HttpClient 单例 `AllowAutoRedirect=false` 跟重定向校验直链，请确认跟重定向有步数上限，避免无限循环。

**本轴最坏项**：`Modules/Activation.cs:254-264` — 联网下载的可执行脚本未哈希钉扎 + 裸 `powershell.exe` 路径劫持风险（Med，无高危注入但属信任边界缺口）。

**已确认良好实践**：PowerShell 调用全经 `Exec.RunPS/RunPowerShell/RunPowerShellGet`（`-EncodedCommand`，无 `-Command` 拼接）；下载普遍 SHA256 校验（SoftwareInstall/ChocolateyResolver）；路径优先 `SystemDirectory`/`GetTempPath`/`SpecialFolder`，无硬编码 `C:\`；进程统一超时+Kill；提权用 `runas` + `IsRunningElevated` 检测；探针 URL 屏蔽 `file://` 非 http(s)。

---

## 汇总

- **逻辑/控制流轴**：4 项（Med×2 / Low×2）。最坏：`Probe.cs:410` 空引用误报。
- **冗余/死代码轴**：约 10 处（多为重复脚手架/模板，维护成本，非运行时 bug）。最坏：4 份标题栏脚手架重复。
- **安全/健壮性轴**：10 项（Med×1 / Low×9）。最坏：`Activation.cs` MAS 裸 powershell + 无哈希钉扎。

**建议修复优先级**
1. 🔴 `Probe.cs:410` 空引用 — 真实功能缺陷，影响探针正确性，建议立刻修。
2. 🟠 `ProbeBrowserHost.cs:436` 潜在永久挂起 — 边界场景可挂起探针线程，建议加异常兜底。
3. 🟠 `Activation.cs:254-264` — 联网脚本哈希钉扎 + 走 `Exec` 完整路径，缩小信任边界。
4. 🟡 `Maint.cs:694` catch 吞异常、`EdgeCore`/`OfficeInstall` 下载校验、`ChocolateyResolver` OData 单引号、`Pages.cs` ToLower 文化敏感 — 中低优先。
5. 🟢 重复脚手架/模板 — 纯维护性重构，可批量提取（建议单独立一个"Dialog 脚手架收敛"任务）。

> 以上均为**审查发现**，未做任何修改。如需我按优先级落地修复，请确认（建议先修 1–3，重构类第 5 点可单独排期）。
