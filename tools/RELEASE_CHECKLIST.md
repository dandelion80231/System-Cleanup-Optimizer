# System-Cleanup-Optimizer 发版流程 Checklist

> 通用版本发布流程（由 v1.0.4 发布复盘总结）。**推 tag ≠ 发布完成**，必须按本清单逐条走查并验证。
> 本机环境：git/gh 出网走 Watt Toolkit 代理 `127.0.0.1:26561`；构建需显式 nuget 源。

---

## 0. 前置准备（网络）

- 发布命令统一加一次性代理环境变量（**不写进 git/系统持久配置、不固化进项目**）：
  ```
  HTTPS_PROXY=http://127.0.0.1:26561 HTTP_PROXY=http://127.0.0.1:26561 <命令>
  ```
- push / release 前先探测代理端口是否可用，避免盲推失败：
  ```
  (exec 3<>/dev/tcp/127.0.0.1/26561) 2>/dev/null && echo OPEN || echo CLOSED
  ```
  若 CLOSED，先确认 Watt Toolkit 已启动，否则会报 `502 ECONNREFUSED 127.0.0.1:26561`。

---

## 1. 代码审查 + 版本号 / 更新日志

- **全量代码审查必须调用 `code-review` skill 执行**（冗余清理 + bug 检查 + 修改），不要凭记忆手动过。
- **版本四处一致**：`csproj(1.0.4.0)` ↔ `APP_VERSION(v1.04)` ↔ `git tag(v1.04)` ↔ 交付文件名版本段（本项目统一两段式 `vX.YY`）。
- ⚠️ **版本号格式必须前后一致**：本项目历史版本均为两段式（`v1.01`/`v1.02`/`v1.03`），**统一沿用两段式 `vX.YY`**（如 `v1.04`）。检查更新 bug 的真正根因是**混用**两段式与三段式——`v1.03`（两段）vs `v1.0.4`（三段）段位错位，导致误判"已高于线上"。**只要所有版本同格式（全两段或全三段），`CompareVersion` 的位置比较就正确**。已发布的 `v1.0.4` 是唯一的异类，下一版改名为 `v1.04` 即与历史对齐，且已装的 v1.03 也会正确收到更新提示。`NormalizeVersion` 保留作防御层（兼容万一出现的混用）。
- **两处更新日志都要补**（易漏）：
  - 仓库 `CHANGELOG.md` 新增对应版本段（Release 附言来源）；
  - **程序内 About 页「更新日志」TextBlock 也要加新版本条目**（区别于 CHANGELOG.md，之前漏过导致 GitHub 显示旧版本）。
- 收尾卫生：辅助 `.ps1` 脚本整理归 `tools/`、**单独提交、不进版本 tag**；排查报告类 `.md` **不纳入发布**（保持 untracked 或移 `docs/`）。

---

## 2. 构建与部署

- ⚠️ **构建前先重新生成嵌入源码包 `src.zip`**：用当前 `src/CpqSystemTool/` 源码（排除 `bin`/`obj`/`.vs` 及旧 `src.zip` 自身）重新打包，确保 exe 内嵌的「导出源码」与版本一致。`dotnet build` 不会自动重打包，`src.zip` 是手动维护的嵌入资源（v1.0.4 曾因漏此步，发布后 exe 内源码包仍是 v1.02）。
- 构建（全局 nuget sources 为空，必须显式加源），目标 **0 错 0 警**：
  ```
  cd src\CpqSystemTool
  dotnet build CpqSystemTool.csproj -c Release --source https://api.nuget.org/v3/index.json
  ```
- 部署：构建产出 `bin\Release\net48\系统清理与优化工具.exe` → 覆盖目标 `系统清理与优化工具_vX.XX.exe` → **SHA256 校验 源=目标 一致**。
- 占用只停 `系统清理与优化工具` 进程（占用时才停，不误伤其它进程）。

---

## 3. README.md 刷新（需提交并推送）

- 版本 badge：`v1.0X` → 新版本。
- 下载说明文件名更新为 `系统清理与优化工具_vX.XX.exe`。
- 分发说明注明：由构建输出 `系统清理与优化工具.exe` 按版本重命名而来。
- 提交并推送（**push 需用户确认，不擅自 push**）。

---

## 4. GitHub Release 创建

- 标题：`系统清理与优化工具 vX.XX`（与历史 release 命名一致，勿只写 `vX.XX`）。
- 附言：取自 `CHANGELOG.md` 对应版本段（可提取该段到临时 notes 文件，用 `gh release create ... --notes-file`）。
- 标记 **Latest**。

---

## 5. 二进制资产上传

- 资产名用**英文名** `System-Cleanup-Optimizer_vX.XX.exe`（与 v1.03 线上资产命名一致；工具内置更新也按此名取 `browser_download_url`）。
- 本地中文交付 `系统清理与优化工具_vX.XX.exe` **复制为英文名再上传**，传完删临时副本。
- ⚠️ **切勿经 Git Bash 向 Windows 版 gh.exe 传中文参数**，否则资产名会被截断为 `_vX.XX.exe`。
- ⚠️ **`gh release create "path#assetname"` 的重命名语法在本机不生效**（会静默回退为文件 basename）。因此**不要依赖 `#` 改名**，直接把临时副本命名为目标英文名 `System-Cleanup-Optimizer_vX.XX.exe` 再上传即可；上传后用 `gh release view vX.XX --json assets` 核验资产名。
- 上传后**必须核验**：
  ```
  gh release view vX.XX --json assets
  ```
  若资产名出现 `_vX.XX.exe`，立即删除重传：
  ```
  gh release delete-asset vX.XX "_vX.XX.exe" -y
  # 重新复制为英文名后：
  gh release upload vX.XX "System-Cleanup-Optimizer_vX.XX.exe" --clobber
  ```

---

## 6. 推送与全程验证

- 推送动作**需用户确认**，不擅自 push。推送（含 tag）：
  ```
  HTTPS_PROXY=http://127.0.0.1:26561 HTTP_PROXY=http://127.0.0.1:26561 git push origin master --tags
  ```
- **每步做完必须验证**：
  - `git ls-remote` 确认 tag 已上远程；
  - `gh release list` 确认 Latest 指向新版本；
  - `gh release view --json assets` 确认资产名 / 大小 / SHA 正确；
  - WebFetch `https://github.com/dandelion80231/System-Cleanup-Optimizer/releases/latest` 确认页面与 README 版本正常。

