# 官方 exe 直链探针（official_exe_finder）

用 [Playwright](https://playwright.dev/) 从软件官网自动挖掘**官方安装包 `.exe` 直链**，并对候选做轻量验证、给出推荐链接。
本目录独立于主程序运行，**不依赖 WorkBuddy 自带的 Node 环境**——首次使用请先跑 `install_deps.ps1` 在本地装好 Node + Chromium。

---

## 1. 用途

给定「入口 URL」或「厂商名」，自动尝试多种策略挖掘官方 exe 直链，并：

- 用 ranged GET 轻量验证直链真伪（状态码 2xx + 二进制 Content-Type）；
- 对每个候选标注命中策略、架构（x64 / x86 / arm64）、验证状态；
- 给出一条**推荐直链**（优先 x64 真 exe）。

---

## 2. 四种探测策略

| 策略 | 说明 |
| --- | --- |
| **静态锚点 (anchor)** | 官网下载页里直接的 `<a href="...exe">`，通过 DOM 查询 + 整页正则提取。 |
| **download 事件 (download)** | 点击「立即下载 / 下载」按钮触发浏览器下载，`page.on('download')` 捕获真实直链（不落盘）。 |
| **JSONP 配置 (jsonp)** | 官网配置 JS / JSONP 下发的服务端签名直链（如 QQ 音乐 `file_redirect.fcg?sign=`）。 |
| **重定向跟随 (redirect)** | 短链 / 中转域名 302 重定向下到真 exe（HttpClient 手动跟随，最多 8 跳）。 |

### ⚡ 无浏览器快速路径（性能关键）

为把抓取耗时从「>15 秒」压到「1~2 秒」，脚本对**静态 HTML / JSONP 下载页**走一条**完全不启动 Chromium** 的快速路径：

1. 直接用 Node 内置 `http`/`https` GET 入口页（强制 `Accept-Encoding: identity`，自动跟随 3xx 重定向，体积上限 5MB）；
2. 扫描响应体里的 `.exe` 直链（`EXE_URL_RE`）与腾讯系签名直链（`FILE_REDIRECT_RE`）；
3. 对候选并行做 ranged GET 验证，**只要验证出一个「真 exe」就直接返回结果**，跳过 Chromium。

已知**必须 JS 交互**（点击按钮 / 弹窗才出直链）的站点（`aliyunpan` / `raylink` / `xshell` / `123pan` 及其别名）被标记为 `JS_HEAVY`，直接跳过快速路径、走 Chromium 兜底。其余站点（如 `qqmusic` / `douyin` / `sogou` / 以及全名搜索定位到的静态下载页）命中快速路径后，**全程零 Chromium 开销**，实测 ~1.2s 出结果。

> Chromium 改为**懒启动**：仅在确实需要时才启动（全名搜索 / 快速路径回落），纯快速路径场景连浏览器都不开。

---

## 3. 命令行用法

```powershell
# 进入探针目录
cd D:\电脑桌面\cpq\tools\probes

# 方式 A：用本地 Node 直接跑（依赖装好后）
.tools\node\node.exe official_exe_finder.js <入口URL或厂商名> [--json] [--proxy=http://127.0.0.1:26561] [--no-download-check]

# 方式 B：先装好依赖（Node + Chromium），只需执行一次
powershell -NoProfile -ExecutionPolicy Bypass -File install_deps.ps1
```

参数说明：

- `<入口URL或厂商名>`：可传多个，空格分隔。厂商名会被映射成入口 URL（见下表）。
- `--json`：仅输出 JSON（末尾带结构化结果），便于程序解析；不带此参数则同时输出人类可读报告 + 末尾 `===JSON===` 块。
- `--proxy=<url>`：指定代理（如 Watt Toolkit `http://127.0.0.1:26561`）。不指定时沿用环境 `HTTPS_PROXY` / `HTTP_PROXY`。
- `--no-download-check`：跳过「点击下载按钮」检测（策略 2），速度更快，但可能漏掉需点击才出现的直链。

---

## 4. 内置厂商名映射

| 厂商名 | 支持的别名 | 入口 |
| --- | --- | --- |
| QQ | `qq`、`qqnt`、`腾讯qq`、`pcqq` | https://im.qq.com/pcqq/ |
| QQ 音乐 | `qqmusic`、`qq音乐` | JSONP 签名直链 |
| 抖音 | `douyin`、`抖音`、`抖音pc`、`抖音电脑版` | https://www.douyin.com/downloadpage |
| 搜狗拼音 | `sogou`、`搜狗`、`搜狗拼音`、`搜狗输入法`、`sogoupinyin` | https://pinyin.sogou.com/ |
| 123 云盘 | `123pan`、`123云盘` | https://www.123pan.com/ |
| 阿里云盘 | `aliyunpan`、`阿里云盘`、`alipan`、`阿里网盘` | https://www.aliyundrive.com/download |
| RayLink | `raylink`、`瑞联` | https://www.raylink.live/download.html |
| Xshell | `xshell`、`xshell7`、`netsarang` | https://www.netsarang.com/en/xshell/ |

> 输入 `http(s)://` 开头的 URL 会原样探测；不在别名表的名称也会原样交给 Playwright 尝试，失败则优雅降级（不会崩溃）。

> **仅商店分发的厂商**：部分应用（如 `deepseek` / `深度求索`）官方**只上架应用商店**（Microsoft Store / App Store / 各大安卓市场），没有官方直接 `.exe` 直链。这类名称被刻意排除在上面的别名表之外——探测它们会直接返回 `error: "STORE_ONLY"` 并给出商店指引，而不会去网上搜那些同名仿冒安装包（有木马风险）。详见第 6 节输出字段。

### 全名搜索（自动定位官网）

当输入的既不是 `http(s)://` 开头、也没命中上面的别名表时，探针会把它当作**软件全名**，例如「微信」「网易云音乐」，自动用 **Bing 中文搜索**（查询词为 `软件名 官方 下载 windows`）定位官网下载页，取首个**非广告**自然结果作为入口 URL，再走上面 4 种抓取策略。

- 搜索命中的结果会在 JSON 输出里带 `source: "search"`（命中别名表为 `"vendor"`，URL 直抓为 `"url"`），主程序「维护工具」页会据此在「推荐直链」下方提示「（此结果由搜索引擎定位，可能非官方直链，请核对）」。
- 搜索引擎常把**应用商店页**（Microsoft Store / App Store / Google Play 等）排在前列，这些页不含可直接抓取的 `.exe`。探针会**主动跳过**商店类域名；若搜到的**全是**商店页，则返回 `error: "STORE_ONLY"` 并给出商店指引，而不是空跑探测。
- 若搜索未定位到官网，探针会输出 `[!] 未找到「<名称>」的官网，请直接输入官网下载页 URL 重试` 并优雅退出（退出码 0，不崩溃）。
- ⚠️ **提醒**：搜索结果可能并非最精确的下载页，抓取到的直链请**人工核对后再写入 `SOFTWARE_LIST`**，尤其注意排除广告/第三方下载站。搜索引擎首位也常是仿冒/聚合站（如随机子域的垃圾站），其返回的直链**切勿直接信任**。

---

## 5. 首次使用：安装本地依赖

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File install_deps.ps1
```

脚本会（以脚本所在目录为基准）：

1. 检查 `.tools\node\node.exe`，不存在则从 nodejs.org 下载 **Windows x64 便携 zip** 并解压到 `.tools\node`；
2. 用本地 Node 在 probes 目录执行 `npm install`（安装 playwright）；
3. 设置 `PLAYWRIGHT_BROWSERS_PATH=0` 后执行 `npx playwright install chromium`，把 Chromium 也装到本地 `node_modules` 下；
4. 给出**中文**成功 / 失败提示。

装好后即可用 `.tools\node\node.exe official_exe_finder.js ...` 运行，不再依赖任何外部环境。

---

## 6. 输出字段解释

`--json` 输出是一个数组，每个元素对应一个入口：

- `entryUrl`：实际探测的入口地址（厂商名已展开）。
- `error`：探测过程中的错误（无则省略 / 为空）。特殊取值：
  - `"STORE_ONLY"`：该应用官方仅通过应用商店分发，无直接 `.exe` 直链（见于 `deepseek` 等，或搜索引擎返回的入口全是商店页）。此时会附带 `storeUrl`（商店页 URL）或 `storeNote`（人工说明），`candidates` 为空，请引导用户去对应商店安装。
  - `"NOT_FOUND"`：全名搜索未定位到任何官网。
- `storeUrl`：当 `error: "STORE_ONLY"` 且由商店页/URL 触发时，给出商店页地址。
- `storeNote`：当 `error: "STORE_ONLY"` 且由 `STORE_ONLY_VENDORS`（如 deepseek）触发时，给出商店指引说明。
- `candidates`：候选直链数组，每条含：
  - `url`：直链地址；
  - `strategy`：命中策略（`anchor` / `download` / `jsonp` / `redirect`，可组合用 `+`）；
  - `isX64` / `isArm64` / `isX86`：架构判定；
  - `denylisted`：是否被旧版黑名单命中；
  - `verified`：是否经 ranged GET 验证为真 exe（`true` / `false`）；
  - `status`：验证时的 HTTP 状态码（或 `SKIP` / `TIMEOUT` / `ERR:...` 等）；
  - `ct`：响应 Content-Type；
  - `redirects`：重定向跳数。
- `recommended`：推荐直链对象（同 `candidates` 结构），可能为 `null`。
- `strategies`：四种策略各自的命中统计（数组）。
- `fastPath`：布尔，`true` 表示该入口走的是「无浏览器快速路径」（未启动 Chromium），`false` 或省略表示走了 Chromium 兜底路径。

主程序「维护工具」页会解析 `candidates` 填充结果面板，并把 `recommended` 突出显示。

---

## 7. 目录结构（安装依赖后）

```
probes/
├── official_exe_finder.js   # 探针主脚本
├── package.json             # 仅 playwright 依赖
├── install_deps.ps1         # 本地依赖安装脚本
├── README.md
├── .gitignore
├── node_modules/            # npm install 产物（被 .gitignore 忽略）
│   └── playwright-core/.local-browsers/  # ⚠️ 这才是 Chromium 引擎本体（约 700MB），
│                                         #    删了 qq/aliyunpan 等站就抓不了，切勿手删！
└── .tools/                  # 本地 Node + Chromium（被 .gitignore 忽略）
    └── node/
        ├── node.exe
        ├── npm.cmd
        └── ...
```
