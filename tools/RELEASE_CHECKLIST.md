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
- **两处更新日志都要补并同步措辞**（易漏）：
  - 仓库 `CHANGELOG.md` 新增对应版本段（Release 附言来源）；
  - **程序内 About 页「更新日志」TextBlock 也要加新版本条目**（区别于 CHANGELOG.md，之前漏过导致 GitHub 显示旧版本）。
  - About 页已改为运行时读取嵌入的 CHANGELOG.md（单一事实来源），发布前无需手动同步 About 措辞；CHANGELOG.md 即权威内容。
  - ⚠️ **CHANGELOG.md 是内嵌资源（csproj:50 `<EmbeddedResource Include="..\..\CHANGELOG.md" LogicalName="CHANGELOG.md" />`）**：任何对它的编辑都必须重新 `dotnet build` + 重部署 exe + 重传资产 + force-move tag，**不能只改文档或 Release 附言**——否则程序内「更新日志」仍显示旧内容（v1.07 曾因只改 CHANGELOG 未重建，被用户纠正）。这与刷新 Release 的「源码有变更须 rebuild」一致。
- 收尾卫生：辅助 `.ps1` 脚本整理归 `tools/`、**单独提交、不进版本 tag**；排查报告类 `.md` **不纳入发布**（保持 untracked 或移 `docs/`）。

---

## 2. 构建与部署

- ⚠️ **【v1.19 起】`src.zip` 已废弃并从仓库删除**：旧方案把手工维护的 `src.zip` 嵌进 exe，它是历史快照（会导出过时代码）。现改为 csproj `GenerateSourcePackage` 目标在**每次构建时自动**重新打包当前源码并内嵌（详见下方「发布」小节）。因此**构建前无需、也不能再手工生成 src.zip**；`tools/regen_src_zip.py` 已成孤儿脚本，可删除。
  - 「导出源码」在**单文件 exe 的任意位置均可使用**（内嵌包自包含），**不再依赖同级 `src/` 目录**。`-Folder` 文件夹构建现已非必需，仅作额外冗余（会再附带一份源码目录）。
- 构建（全局 nuget sources 为空，必须显式加源），目标 **0 错 0 警**：
  ```
  cd src\CpqSystemTool
  dotnet build CpqSystemTool.csproj -c Release --source https://api.nuget.org/v3/index.json
  ```
- **发布（默认单文件 exe，与历史分发形态一致）**：用脚本 `tools/publish.ps1` 产出**单文件 exe** `publish_single_vX.XX\系统清理与优化工具.exe`（框架依赖单文件，与历史 `系统清理与优化工具_vX.XX.exe` 同形态）：
  ```
  powershell -ExecutionPolicy Bypass -File tools/publish.ps1 -Version vX.XX            # 框架依赖单文件，体积小（默认分发物）
  powershell -ExecutionPolicy Bypass -File tools/publish.ps1 -Version vX.XX -SelfContained   # 自包含单文件，免装 .NET 运行时，体积大
  ```
  - ⚠️ **【v1.19 起】源码披露包改为「构建期内嵌」**（csproj `GenerateSourcePackage` 目标，`BeforeTargets="BeforeBuild"`）：每次构建都把当前源码重新打包成 `CpqSystemTool.srcpkg.zip` 内嵌，**体积小（约 0.83MB）且永远是当前源码**（旧方案手工维护 `src.zip`，内嵌的是历史快照、会导出过时代码）。
    - **刻意排除两张背景图**（`background.png` / `background-light.png`，合计 2.21MB，占包体积 73%）：它们本来就以 `<Resource>` 嵌在程序集内，导出时由 `MainWindow.Config.cs` 的 `CpqExtractBackgroundsFromAssembly` 用 `Application.GetResourceStream` 从运行中的程序集取回——不重复占体积，导出结果依然完整可编译。
    - 同时排除 `bin/obj/.vs/.git/packages` 与 `Microsoft.Web.WebView2.Core.dll`（NuGet 依赖，非源码披露范围）。
    - 效果：**单文件 exe 在任意位置运行都能导出源码**（不再依赖同级 `src/`）。导出逻辑优先解包内嵌包，失败才回退到目录复制（文件夹发布/仓库内运行）。
    - 已实测：内嵌包 0.83MB（64 `.cs` + 56 `.xrm-ms` + README），exe 5.64MB → 6.47MB，导出结果含 64 `.cs` + 2 `.xaml` + 1 `.csproj` + 56 授权 + 2 背景图 + README。
  - （可选，非必需）`-Folder` 产**文件夹构建**（不传 PublishSingleFile，csproj `CopySourceDisclosure` 额外把源码复制到 `src/CpqSystemTool` 作冗余），再 `-Zip` 可连带打包：
    ```
    powershell -ExecutionPolicy Bypass -File tools/publish.ps1 -Version vX.XX -Folder        # 文件夹构建，导出可用
    powershell -ExecutionPolicy Bypass -File tools/publish.ps1 -Version vX.XX -Folder -Zip   # 文件夹构建 + zip 包
    ```
  - 产出校验：脚本确认 exe 存在、打印 exe 的 SHA256；`-Folder` 模式额外确认 `src\CpqSystemTool` 含 .cs 文件。
- **分发**：网站/Release 托管的是**单个 exe** `系统清理与优化工具_vX.XX.exe`（由 `publish_single_vX.XX\系统清理与优化工具.exe` 按版本重命名而来），**不是文件夹 zip**。
  - 占用只停 `系统清理与优化工具` 进程（占用时才停，不误伤其它进程）。

---

## 3. README.md 刷新（需提交并推送）

- ⚠️ **功能增删改必须同步 README 正文**（v1.05 曾因只改 badge/文件名，导致 README 仍是旧 12 功能页、缺新增「驱动清理」模块）。升版/加功能时逐项核对并同步：
  - **目录**：新增/移除对应功能锚点；
  - **功能概览**：主功能页计数 + 表格行同步；
  - **功能详解**：新增整节时，类名/方法名须对照真实源码（如 `Modules/DriverStore.cs`、`DriverStorePanel.cs`、`MainWindow.DriverStore.cs`），不得编造；
  - **项目结构**：新增的 `MainWindow.*.cs`/模块文件、Build* 方法、`Modules/` 计数同步。
- 版本 badge：`v1.0X` → 新版本。
- 下载说明文件名更新为 `系统清理与优化工具_vX.XX.exe`。
- 分发说明注明：由构建输出 `系统清理与优化工具.exe` 按版本重命名而来。
- README 内容须与 `CHANGELOG.md` + 程序内实际功能**三处一致**（code-review 时一并核对，见 Step 1）。
- 提交并推送（**push 需用户确认，不擅自 push**）。

---

## 4. GitHub Release 创建

- ⚠️ **创建 Release / 打 tag 之前，源码须已提交并 push 到远程**（若需「导出源码」功能对下载者可用，先用 `tools/publish.ps1 -Version vX.XX -Folder` 产出含最新 `src/` 的文件夹构建）：GitHub 自动生成的 `Source code (zip)` / `Source code (tar.gz)` 是基于创建 Release 时的 tag commit 快照生成的，**不会随后续 commit 自动刷新**。若发生过期，按第 5 步上传 `System-Cleanup-Optimizer_vX.XX_src.zip` 资产补救。
- 标题：`系统清理与优化工具 vX.XX`（与历史 release 命名一致，勿只写 `vX.XX`）。
- 附言：取自 `CHANGELOG.md` 对应版本段（可提取该段到临时 notes 文件，用 `gh release create ... --notes-file`）。
- 标记 **Latest**。

---

## 5. 二进制资产上传

- ⚠️ **上传 / push 前最后核对 `.gitignore`**：先 `git status --short` 确认仓库根目录无遗留未跟踪的本地产物（调查类 `.md`、`pnputil_*.txt` 等命令输出、`site-dist/` 部署目录等）；若这些文件未被 `.gitignore` 覆盖，先补进 `.gitignore` 再继续，杜绝 `git add -A` / `git add .` 误提交非项目文件（曾因根目录遗留报告文档导致需事后清理）。
- 资产名用**英文名** `System-Cleanup-Optimizer_vX.XX.exe`（与 v1.03 线上资产命名一致；工具内置更新也按此名取 `browser_download_url`）。
- 本地中文交付 `系统清理与优化工具_vX.XX.exe` **复制为英文名再上传**，传完删临时副本。
- ⚠️ **切勿经 Git Bash 向 Windows 版 gh.exe 传中文参数**，否则资产名会被截断为 `_vX.XX.exe`。
- ⚠️ **`gh release create "path#assetname"` 的重命名语法在本机不生效**（会静默回退为文件 basename）。因此**不要依赖 `#` 改名**，直接把临时副本命名为目标英文名 `System-Cleanup-Optimizer_vX.XX.exe` 再上传即可；上传后用 `gh release view vX.XX --json assets` 核验资产名。
- ⚠️ **必须同时上传对应版本的 `README.md` 作为 Release 资产**（v1.04 起要求；v1.06/v1.07 曾一度漏传，已补）：用户下载 exe 时可一并下载功能介绍，弥补「exe / 导出源码包不含 README」的缺口。从当前 tag 取 `README.md`（与版本 badge/下载文件名/功能说明一致），与 exe 一起上传、一起核验：
  ```
  gh release upload vX.XX "System-Cleanup-Optimizer_vX.XX.exe" "README.md" --clobber
  ```
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

---

## 7. 刷新已有 Release（不打新 tag，仅更新内容）

> 场景：GitHub 上已有某版本 Release，本轮只推了 `master` + 上传了二进制，但发现 Release 页的**源代码(zip/tar.gz)、README 资产、更新描述仍是旧 tag 的内容**。根因：这些元素**绑定到 tag 指向的 commit**，仅推 master / 上传二进制**不会**刷新它们。

⚠️ **正确做法（更新现有 tag，非新建）：**

> ⚠️ **若本轮源码有变更（bug 修复、About/CHANGELOG 改动等），须先 `tools/publish.ps1 -Version vX.XX` 重建单文件 exe（需要导出可用则加 `-Folder`）、再部署 exe**，然后才移动 tag。`publish.ps1` 位于仓库 `tools/`。

1. 先把 `CHANGELOG.md` 对应版本段补齐（🐛 修复 / ♻️ 打磨 等），提交到 `master`（得到新 HEAD，如 `2cc5e01`）。
2. 移动现有 tag 到新 HEAD 并强制推送：
   ```
   git tag -f vX.XX <newHEAD>
   git push origin vX.XX --force        # forced update，远程 tag 指向新 commit
   git push origin master               # 让分支追上新 HEAD，与 tag 对齐
   ```
   GitHub 会据此**重新生成** Source code 源码包（来自新 commit）。
3. 更新 Release 描述正文：`gh release edit vX.XX --notes-file <vX.XX changelog>`。
4. **重新上传所有「上传型」资产**（不为自动生成）：README.md 在 Release 上是当初上传的资产，旧 tag 时是旧版，必须重传覆盖：
   ```
   gh release upload vX.XX "README.md" --clobber
   ```
5. 核验 tag 内某文件内容，最稳用 **contents API（base64 解码）**，避开 zipball 坑：`gh api repos/<owner>/<repo>/contents/CHANGELOG.md?ref=vX.XX` 取 `content` 字段 base64 解码后 grep 目标串（如 `二次修补`/`统一全部 ComboBox`）。⚠️ `gh api .../zipball/vX.XX --silent > f` 会得 **0 字节**（`--silent` 吞掉重定向/报错），勿用。另 `gh release view --json assets` 确认资产名与大小。

📌 **关键认知**：「不打新 tag」≠「不碰 tag」。要刷新已存在 Release 的内容，必须**移动现有 tag（force-push）并重新上传上传型资产**；否则源码/README/描述始终停在原 tag 的旧 commit。这与「不打新 tag」不冲突（是更新现有 tag，不是新建 `vX.YY`）。

---

### 8. 发版收尾：同步官网（Cloudflare Pages 静态站）

> 场景：GitHub Release 与二进制已上传（Step 4–5）。官网 `https://cpq-system-tool.pages.dev/` 是**独立静态站**（`D:\电脑桌面\cpq\site-dist`），**不会随 GitHub 自动更新**——必须手动把「新安装包 + 新更新日志」同步进去并重部署，否则官网下载页 / 更新日志停在旧版。

⚠️ 官网与 GitHub 是**两份独立内容**：官网下载页**直接托管中文名 exe**（`系统清理与优化工具_vX.XX.exe`），不是跳 GitHub。GitHub Release 资产仍用英文名（Step 5），两套命名并行。每次发版都要补这最后一脚。

1. **准备安装包**：
   - 本地交付物已经是中文名 `系统清理与优化工具_vX.XX.exe`。
   - 把它复制到 `D:\电脑桌面\cpq\site-dist\` 作为官网托管包。
   - 同时保留英文名副本 `System-Cleanup-Optimizer_vX.XX.exe` 给 GitHub Release 用（Step 5）。
   - 算好中文包的哈希与大小备用：
     ```
     Get-FileHash -Algorithm SHA256 "D:\电脑桌面\cpq\site-dist\系统清理与优化工具_vX.XX.exe"
     (Get-Item "D:\电脑桌面\cpq\site-dist\系统清理与优化工具_vX.XX.exe").Length   # 字节数
     ```
2. **更新 `download.html`（两栏布局，用脚本，禁止手改或旧脚本）**：
   ⚠️ **`download.html` 的版本面板是硬编码静态内容**（render_site 只把 version.json 喂给其它页「最新版」横幅，不驱动 download.html 多版本列表）。加版本必须用脚本 `tools/add_site_version.py`：
   ```
   python tools/add_site_version.py --version vX.XX --date YYYY-MM-DD \
       --size <字节数> --sha256 <64hex> \
       --changelog <本版更新日志内部HTML文件> [--apply]
   ```
   - **三处契约（不守就布局崩）**：加版本必须**同时**改三处且顺序一致、`active` 唯一对齐到新版本：左栏 tab `.dl-tab[data-ver]` / 左栏 panel `.dl-panel[data-panel]` / 右栏 chlog `.chlog-panel[data-panel]`。
   - `--changelog` 必填（含本版更新日志内部 HTML：`<blockquote>`+`<h4>`+`<ul>`），脚本包成 `chlog-panel` 插右栏——这是旧 4 个脚本全漏的一步。
   - 默认 dry-run；确认无误加 `--apply`（先备份再写）。写文件前做完整契约自检，失败绝不写入。
   - 自动同步 `version.json` / `versions.json`（新版本 `is_latest=true`）。
   - ❌ 严禁 `tools/_deprecated/` 下 `add_v117_only.py`/`add_version_panel.py`/`create_v117_template.py`/`sync_changelog.py`（已实测全破坏布局）。
   - 历史版本英文包名逐步改中文名（重命名 site-dist 旧 exe 并同步旧面板链接）。
3. **更新 `changelog.html`（单一来源，禁止手抄）**：时间线由 `CHANGELOG.md` 经 `tools/sync_changelog_to_site.py` 重新生成，**不再手抄** `.tl-item` 内容。新流程：
   ① 在仓库根 `CHANGELOG.md` 按既有格式新增/修改对应版本段（blockquote `> 相对 vX 的源码变更…` + `### 分类标题` + `- 条目`；二级缩进 `  - ` 会自动渲染为嵌套 `<ul>`，`---` 分隔线会被跳过）；
   ② 运行 `python tools/sync_changelog_to_site.py --apply --render` 重新生成 `download.html` 右栏 `chlog-panel` 与 `changelog.html` 时间线（render 会顺带生成 `site-dist`）；
   ③ 跑 Step 6 的 `validate_site.py` + `validate_html.py` 校验（div 平衡 + 三栏契约 + active 唯一 + 0 处 `<p>---</p>`）；
   ④ 部署（Step 7）。
   - ⚠️ 新版本若 `changelog.html` 尚无对应 `.tl-item`，需先补一个空骨架 `<div class="tl-item"><span class="ver">vX.XX</span><span class="date">YYYY-MM-DD</span><ul></ul></div>`（置于时间线顶部/对应位置），sync 会用 `CHANGELOG.md` 内容填满 `<ul>`；`download.html` 新增版本则用 `add_site_version.py`（Step 2）补三栏骨架。`sync` 只刷新**已存在**的面板/条目，不会凭空新建版本。
   - 旧「手抄 changelog.html 要点 `<ul>`」做法已废弃——改 `CHANGELOG.md` 再 sync 即可，避免两处漂移。
4. **（可选）同步功能页**：本版动了功能 / 模块时，同步 `features.html` 对应模块与 `index.html` 卡片（保持与 README 三处一致，见 Step 3）。
5. **保持约定**：内部链接一律**无后缀**（`features` / `download` / `changelog` / `/`），不要写回 `xxx.html`——Cloudflare Pretty URLs 会对 `.html` 做 308 重定向拖慢切页；每页 `<head>` 保留对其他兄弟页的 `<link rel="prefetch">`。
6. **校验（两步都跑）**：
   - `python tools/validate_site.py site-src/download.html` —— **契约强校验**（div 平衡 + tab/panel/chlog 三处数量相同且顺序一致 + active 唯一对齐 + 无废弃单栏类名）。`validate_html.py` 只查通用标签嵌套、查不出契约断裂。
   - `python tools/validate_html.py site-src/*.html` —— 四页通用标签平衡。
   - 两步全 OK 再部署。
7. **部署（必须用 managed venv 解释器，脚本依赖 `blake3`）**：
   - 🔑 **CF token 从本地文件读取（不再要求用户每次提供）**：token 保存在用户级私有文件 `C:\Users\000\.workbuddy\cf_api_token.md`（**不在任何 git 仓库内**，禁止提交/上传/分享；泄露后到 Cloudflare 后台吊销轮换并更新该文件）。部署时用 shell 从文件提取：
   ```
   CF_ACCOUNT_ID=$(grep -oP '^CF_ACCOUNT_ID=\K.*' "C:\Users\000\.workbuddy\cf_api_token.md") \
   CF_API_TOKEN=$(grep -oP '^CF_API_TOKEN=\K.*' "C:\Users\000\.workbuddy\cf_api_token.md") \
     C:\Users\000\.workbuddy\binaries\python\envs\default\Scripts\python.exe D:\电脑桌面\cpq\tools\deploy_site.py
   ```
   - 若文件缺失或提取为空：先提示用户确认文件存在（路径 `C:\Users\000\.workbuddy\cf_api_token.md`），再请其重新提供 token 并更新该文件；**切勿把 token 写进本文件或任何 git 跟踪的文件**。
   - 脚本重传 site-dist 全部顶层文件（含 **17 个 exe（v1.01–v1.17），约 114MB**），完整上传约 4–6 分钟属正常。⚠️ **本 agent 运行时后台任务约 2 分钟会被掐断**，务必**前台运行并给足超时**（Bash 工具设 `timeout=540000`）再部署；若中途报 `failed` 且停在 `Step2 batch 7/10` 左右，就是被掐了，前台重跑一次即可。
   - 脚本内含 IPv4 强制解析 monkeypatch（Python 默认 IPv6 优先对 Cloudflare 握手失败），部署 API 无需额外代理。
   - 📌 **缓存策略与安全响应头现由 `tools/_worker.js`（Pages Functions）在每个响应上统一注入**：`Cache-Control`（指纹资源 `*.css|js|ico|exe|…` → `immutable` 长缓存；HTML → `max-age=300`）+ `Strict-Transport-Security` / `X-Frame-Options: DENY` / `X-Content-Type-Options: nosniff` / `Referrer-Policy` / `X-Robots-Tag`（4xx 页 `noindex`）。**Direct Upload 下 `_headers` 被 Functions 忽略**，要改缓存 TTL 或安全头请直接改 `_worker.js` 后重部署；`deploy_site.py` 会自动把 `tools/_worker.js` 复制进 site-dist 并作为独立 part 上传，无需手动处理。
8. **验证（部署完必须真验，不能只信脚本成功消息）**：用 `?ts=<时间戳>` 绕边缘缓存，⚠️ 注意 Cloudflare 陷阱（已实锤）：
   - `/download.html` 被 308 重定向到 `/download` —— 验收页面用 `curl -sL .../download`。
   - `_worker.js` 不响应 HEAD（`curl -I` 对 exe 返回空）—— 用 GET 取头：`curl -s -D hdr.txt -o /dev/null --max-time 12 <URL>` 解析 `hdr.txt`。
   - **soft-404**：不存在文件返回 `200 + text/html`，所以判据只能是 `Content-Type: application/octet-stream` + `Content-Length` 精确等于本地字节数；每次带一个不存在版本号反向对照。
   - ETag 不是内容 MD5，字节级确认需完整下载算 SHA256。
   - 验收项：`download` 页含新版本 tab 与 exe 链接且 `validate_site.py` 复核通过；`/系统清理与优化工具_vX.XX.exe` 响应 `application/octet-stream` 且 `Content-Length` 等于本地；抽样下载新 exe 算 SHA256 与页面显示 + 本地三方一致；四页可访问、无 `.html` 内部链接。

📌 **关键认知**：官网不是 GitHub 的镜像，发版最后一步必须手动同步并重部署；漏这步官网停在旧版（与「只推 tag 不算发布」同理）。

