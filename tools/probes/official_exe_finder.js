#!/usr/bin/env node
/**
 * official_exe_finder.js —— 官方软件安装包 exe 直链通用探针
 * ------------------------------------------------------------------
 * 用途：给定一个或多个厂商「入口 URL / 厂商名」，自动尝试多种策略，
 *       挖掘官方 .exe 直链，并做轻量 ranged GET 验证其真伪，给出推荐链接。
 *
 * 已验证可用的 4 种策略（详见同目录 SKILL.md）：
 *   1) 静态锚点：官网下载页直接 <a href="...exe">，DOM/正则提取。
 *   2) download 事件：点击「立即下载/下载」按钮触发下载，page.on('download') 捕获直链。
 *   3) JSONP 配置接口：官网配置 JS/JSONP 下发服务端签名的真实直链（如 QQ音乐 file_redirect.fcg?sign=）。
 *   4) 重定向跟随：短链/中转域名 302 重定向下到真 exe（HttpClient 手动跟随）。
 *
 * 运行约束（随 official_exe_finder 探针分发，见 install_deps.ps1）：
 *   - Node：优先系统 PATH 的 node，否则用本地 .tools\node\node.exe（install_deps.ps1 下载）。
 *   - playwright 解析自本目录 node_modules（install_deps.ps1 执行 npm install 产出），
 *     故脚本直接放在 probes 目录即可运行，无需塞进其它 workspace。
 *   - Chromium 由 PLAYWRIGHT_BROWSERS_PATH=0 装到 node_modules\{playwright-core,playwright}\.local-browsers，
 *     启动参数 headless + --no-sandbox；静态/JSONP 下载页走无浏览器快速路径，不启动 Chromium。
 *   - 网络走本地代理 Watt Toolkit（127.0.0.1:26561）；未显式 --proxy 时由环境 HTTPS_PROXY 自动接管。
 *
 * 用法：
 *   node official_exe_finder.js <入口URL或厂商名> [更多URL/厂商名...] [--json] [--proxy=http://127.0.0.1:26561] [--no-download-check]
 *
 * 输出：人类可读报告 + 末尾 JSON 块（--json 时仅输出 JSON）。
 */

const { chromium } = require('playwright');
const https = require('https');
const http = require('http');
const { URL } = require('url');

// ===================== 全局配置 =====================
const UA = 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36';
const PER_SITE_TIMEOUT = 30000;            // 单站超时 ≤30s
const VERIFY_TIMEOUT = 15000;              // 单链接验证超时
const MAX_REDIRECT = 8;                    // 重定向跟随上限

// 策略 2（点击下载类按钮）的有界参数：避免兜底路径被拖到过长，同时覆盖 Coze 等两级菜单
const CLICK_STRATEGY_MAX_PASS_CLICKS = 6;  // 每遍最多点击次数
const CLICK_TIMEOUT_MS = 1200;             // 单次点击超时
const POST_CLICK_SETTLE_MS = 500;          // 点击后等待页面稳定
const DOWNLOAD_EVENT_TIMEOUT_MS = 2000;    // 等待自然 download 事件

// 二进制 exe 内容类型（用于验证「真 exe」）
const EXE_BIN_CT = /application\/octet-stream|application\/x-msdownload|application\/x-msdos-program|application\/force-download|binary/i;

// 手机 / macOS 安装包后缀：本工具面向 Windows，这类直链对 Windows 用户无意义，直接跳过（避免 Coze 等站点把 APK/IPA/DMG 当候选）
const MOBILE_MAC_PKG_RE = /\.(apk|ipa|dmg|app|pkg|deb|rpm)(\?|$)/i;

// 从任意文本抽取 .exe 直链的宽松正则
const EXE_URL_RE = /https?:\/\/[^\s"'<>()\\]+?\.exe(?:\?[^\s"'<>()\\]*)?/gi;
// 腾讯系 file_redirect 签名直链（JSONP 配置策略核心特征）
const FILE_REDIRECT_RE = /https?:\/\/[^\s"'<>()\\]*?file_redirect\.fcg[^\s"'<>()\\]*/gi;

// 厂商名 → 入口 URL 映射（已验证站点，详见 SKILL.md）。
// 每个 key 都支持：英文厂商名、中文产品名、常见简称/别名，大小写不敏感。

// Kimi（月之暗面）Windows 客户端官方 CDN 直链。
// 注意：官网下载入口需登录后才显示，未登录会被 302 重定向回首页，headless 探针看不到下载按钮，
// 因此这里直接写死官方 CDN 直链，由探针做 ranged GET 验证真伪。
// TODO: 版本号 3.1.3 会随官网更新而过时 —— 官网发版后需同步此处（或改为运行时从官网 JSON 配置解析）。
const KIMI_WIN_CDN = 'https://kimi-img.moonshot.cn/app/download/windows/kimi_3.1.3.exe';

// Coze（扣子）Windows 桌面端官方 CDN 直链。
// 原因：coze.cn/overview 虽是真实入口，但 headless Chromium 经多轮点击策略仍无法稳定抓到由 JS/Fetch 下发的直链；
// 用户已提供官方 CDN 真实地址（字节 lf3-cdn-tos.bytegoofy.com），直接写死并由 ranged GET 验证真伪。
// TODO: 版本号 1.1.29 会随官网更新而过时 —— 发版后需同步此处。
const COZE_WIN_CDN = 'https://lf3-cdn-tos.bytegoofy.com/obj/tron-demo/7617773946401724698/447322331/1.1.29/win32-x64/Coze-v1.1.29-win32-x64.exe';

// 官方 CDN 直链兜底：仅当「官网实时抓取」主路径拿不到直链时使用（见 main 兜底逻辑）。
// 适用：coze（JS/Fetch 下发直链，headless 难稳定抓到）、kimi（下载页登录门控，未登录 302 回首页）。
// 版本号会随官网更新而过时（TODO：发版后需同步此处）→ 故仅作保底；若兜底直链 404，探针会明确告警而非静默失效。
const FALLBACK_CDN = {
  coze: COZE_WIN_CDN,
  kimi: KIMI_WIN_CDN,
};

const VENDOR_MAP = {
  // 腾讯 QQ
  'qq':        'https://im.qq.com/pcqq/',
  'qqnt':      'https://im.qq.com/pcqq/',
  '腾讯qq':    'https://im.qq.com/pcqq/',
  'pcqq':      'https://im.qq.com/pcqq/',
  // QQ 音乐
  'qqmusic':   'https://y.qq.com/download/download.js?cv=4747474&ct=24&format=json&inCharset=utf-8&outCharset=utf-8&notice=0&platform=yqq.json&needNewCode=1&uin=0&g_tk_new_20200303=5381&g_tk=5381&jsonpCallback=MusicJsonCallback',
  'qq音乐':    'https://y.qq.com/download/download.js?cv=4747474&ct=24&format=json&inCharset=utf-8&outCharset=utf-8&notice=0&platform=yqq.json&needNewCode=1&uin=0&g_tk_new_20200303=5381&g_tk=5381&jsonpCallback=MusicJsonCallback',
  // 抖音
  'douyin':    'https://www.douyin.com/downloadpage',
  '抖音':      'https://www.douyin.com/downloadpage',
  '抖音pc':    'https://www.douyin.com/downloadpage',
  '抖音电脑版':'https://www.douyin.com/downloadpage',
  // 搜狗拼音
  'sogou':     'https://pinyin.sogou.com/',
  '搜狗':      'https://pinyin.sogou.com/',
  '搜狗拼音':  'https://pinyin.sogou.com/',
  '搜狗输入法':'https://pinyin.sogou.com/',
  'sogoupinyin':'https://pinyin.sogou.com/',
  // 123 云盘
  '123pan':    'https://www.123pan.com/',
  '123云盘':   'https://www.123pan.com/',
  // 阿里云盘
  'aliyunpan':      'https://www.aliyundrive.com/download',
  '阿里云盘':       'https://www.aliyundrive.com/download',
  'alipan':         'https://www.aliyundrive.com/download',
  '阿里网盘':       'https://www.aliyundrive.com/download',
  // RayLink / 瑞联：官网已从 raylink.com（已过期转入 GoDaddy 待售页）迁移到 raylink.live，
  // 下载页 download.html 含「立即下载」按钮。（版本号易失，以官网实时为准，无需在此写死）
  'raylink':   'https://www.raylink.live/download.html',
  '瑞联':      'https://www.raylink.live/download.html',
  // Coze（扣子）：写死官方 CDN 直链为主路径（见 COZE_WIN_CDN 注释）。
  // 说明：coze.cn/overview 的「下载桌面端」直链由 JS/Fetch 下发，headless 经多轮点击策略仍无法稳定抓取（易 0 候选），
  // 故以写死 CDN 保可靠；版本漂移由下方 FALLBACK/404 检测兜底告警（见 main 兜底逻辑）。
  // 若某环境浏览器可稳定抓到 /overview 的实时直链，可将此处改回官网地址走「实时抓取」主路径（无版本漂移）。
  'coze':      COZE_WIN_CDN,
  '扣子':       COZE_WIN_CDN,
  '扣子coze':   COZE_WIN_CDN,
  // Xshell
  'xshell':    'https://www.netsarang.com/en/xshell/',
  'xshell7':   'https://www.netsarang.com/en/xshell/',
  'netsarang': 'https://www.netsarang.com/en/xshell/',
  // Kimi（月之暗面）：官网登录门控，见上方 KIMI_WIN_CDN 注释。
  'kimi':      KIMI_WIN_CDN,
  'kimi智能助手': KIMI_WIN_CDN,
  '月之暗面':  KIMI_WIN_CDN,
  // 微信（WeChat）PC 版：Bing 等搜索引擎常把「微信」首条指向微云/weiyun 下载页（错），
  // 直接写死官方 PC 入口，让探针走无浏览器快速路径或 Chromium 正确挖掘 exe 直链。
  'wechat':    'https://pc.weixin.qq.com/',
  '微信':       'https://pc.weixin.qq.com/',
  'weixin':     'https://pc.weixin.qq.com/',
};

// —— 厂商官方域名白名单（正向约束，本探针「防错站」策略的核心）——
// 用途：搜索/入口解析时，仅接受命中本表（或子域）的结果作为该厂商的「官方站点」。
// 与 WRONG_SITE_HOSTS（黑名单，被动跳过无关站）互补——白名单是正约束，更稳健、可迁移：
// 只要输入能 fuzzy 命中某个厂商别名，搜索引擎哪怕把首条指向 weiyun.com，也绝不会采用，
// 从根本上消除「搜到错站」。每个厂商列出其官网注册域 + 官方 CDN 注册域（直链常落在 CDN 子域，
// 如 Coze 在 bytegoofy.com、微信在 qq.com）。白名单可靠时，搜索只会在官网域抓直链。
// 同时，这也是「实时抓取官网直链、不写死版本」策略能成立的前提：白名单确认了「官网域」，
// 探针即可每次从官网实时取当前版本直链，不存在硬编码版本漂移 404 的风险。
const VENDOR_CANON = {
  'qq': 'qq', 'qqnt': 'qq', '腾讯qq': 'qq', 'pcqq': 'qq',
  'qqmusic': 'qqmusic', 'qq音乐': 'qqmusic',
  'douyin': 'douyin', '抖音': 'douyin', '抖音pc': 'douyin', '抖音电脑版': 'douyin',
  'sogou': 'sogou', '搜狗': 'sogou', '搜狗拼音': 'sogou', '搜狗输入法': 'sogou', 'sogoupinyin': 'sogou',
  '123pan': '123pan', '123云盘': '123pan',
  'aliyunpan': 'aliyunpan', '阿里云盘': 'aliyunpan', 'alipan': 'aliyunpan', '阿里网盘': 'aliyunpan',
  'raylink': 'raylink', '瑞联': 'raylink',
  'coze': 'coze', '扣子': 'coze', '扣子coze': 'coze',
  'xshell': 'xshell', 'xshell7': 'xshell', 'netsarang': 'xshell',
  'kimi': 'kimi', 'kimi智能助手': 'kimi', '月之暗面': 'kimi',
  'wechat': 'wechat', '微信': 'wechat', 'weixin': 'wechat',
};
const OFFICIAL_DOMAINS = {
  qq:        ['qq.com', 'im.qq.com'],
  qqmusic:   ['y.qq.com', 'qq.com'],
  douyin:    ['douyin.com', 'bytedance.com'],
  sogou:     ['sogou.com'],
  '123pan':  ['123pan.com'],
  aliyunpan: ['aliyundrive.com', 'alipan.com'],
  raylink:   ['raylink.live', 'raylink.com'],
  coze:      ['coze.cn', 'bytegoofy.com'],
  xshell:    ['netsarang.com'],
  kimi:      ['kimi.com', 'moonshot.cn'],
  wechat:    ['weixin.qq.com', 'qq.com'],
};

// 判断 host 是否落在某厂商的官方域名白名单内（含子域）。
function isOfficialDomainFor(vendor, host) {
  const list = OFFICIAL_DOMAINS[vendor];
  if (!list || !host) return false;
  host = host.toLowerCase();
  return list.some((d) => host === d || host.endsWith('.' + d));
}

// 由输入名 fuzzy 推断厂商（canonical key），返回 canonical vendor 或 null。
// 用途：① 已知厂商的各种写法（如「微信电脑版」）直接路由到官方入口，跳过不可靠搜索 → 不跳错站；
//       ② 搜索时若需对该厂商施加白名单约束（见 searchVendorPage 可选参数）。
// 匹配：输入 === 别名，或输入包含别名；ASCII 别名额外要求词边界（避免 earthquake⊃qq 误命中），CJK 子串即可。
function inferVendorKey(name) {
  const n = (name || '').toString().toLowerCase().trim();
  if (!n) return null;
  let best = null, bestLen = 0;
  for (const key of Object.keys(VENDOR_MAP)) {
    const k = key.toLowerCase();
    if (k.length < 2) continue;
    let hit = false;
    if (n === k) hit = true;
    else if (n.includes(k)) {
      if (/^[a-z0-9]+$/.test(k)) {
        const esc = k.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
        hit = new RegExp('(^|[^a-z0-9])' + esc + '([^a-z0-9]|$)', 'i').test(n);
      } else hit = true; // CJK 子串即可
    }
    if (hit && k.length > bestLen) { best = key; bestLen = k.length; }
  }
  return best ? (VENDOR_CANON[best] || best) : null;
}

// 已知需要 JS 交互（点击按钮/弹窗才出直链）的站点：直接跳过无浏览器快速路径，
// 省去一次注定失败的 HTTP 扫描 + 回落开销。静态 HTML / JSONP 站点不在此列。
const JS_HEAVY = new Set([
  'aliyunpan', '阿里云盘', 'alipan', '阿里网盘',
  'raylink', '瑞联',
  'xshell', 'xshell7', 'netsarang',
  '123pan', '123云盘',
]);

// 应用商店 / 市场类域名 —— 这些页面只提供商店安装入口（Microsoft Store / App Store / Google Play / 各安卓商店等），
// 不含可直接抓取的 .exe 直链。搜索引擎常以它们作为「厂商名」的首条结果，会掩盖真正的官网下载页，
// 导致探针落到商店页后空手而归。搜索引擎定位官网时应主动跳过；若搜到的全是这类页，则明确标记为「仅商店分发」。
const APP_STORE_HOSTS = new Set([
  'apps.microsoft.com',          // Microsoft Store 商品页（如 DeepSeek 的 Windows 客户端）
  'apps.apple.com',              // App Store
  'play.google.com',             // Google Play
  'appgallery.huawei.com',       // 华为应用市场
  'app.mi.com',                  // 小米应用商店
  'store.oppo.com',              // OPPO 软件商店
  'store.vivo.com.cn',           // vivo 应用商店
  'a.vmall.com',                 // 荣耀/华为
  'sj.qq.com',                   // 应用宝
  'store.steampowered.com',      // Steam
]);

// 搜索引擎「错站」黑名单 —— 与 APP_STORE_HOSTS 类似的误导来源，但性质不同：
// 这些是云盘/下载站/无关镜像页，搜索引擎常把「厂商全名」（如「微信电脑版」）首条指向它们，
// 而非真正的官网（如 weiyun.com 是腾讯微云，却被 Bing 当成「微信」下载页返回）。
// 落到这类页只会拿到页面 URL、无任何 exe 直链，还会被当成「官方搜索结果」误导用户。
// 搜索引擎定位官网（searchVendorPage）时应主动跳过；若优质结果都被它们占满，再降级处理。
// 新增原则：凡观察到某「厂商名」搜索被稳定误导到某无关下载站，就把其 host 加进此表。
const WRONG_SITE_HOSTS = new Set([
  'weiyun.com', 'www.weiyun.com',     // 腾讯微云：Bing 常把「微信」全名搜索首条指向微云下载页（错站）
  'www.weiyun.com.cn',
]);

// 判断 URL 是否为搜索引擎「错站」（云盘/下载站/无关镜像页，不提供 exe 直链，仅误导用户）。
function isWrongSiteUrl(u) {
  try { return WRONG_SITE_HOSTS.has(new URL(u).hostname.toLowerCase()); } catch (_) { return false; }
}

// 判断 URL 是否为应用商店/市场页（按 host，或 microsoft.com/store 路径）。
function isAppStoreUrl(u) {
  try {
    const url = new URL(u);
    const h = url.hostname.toLowerCase();
    if (APP_STORE_HOSTS.has(h)) return true;
    if (h.endsWith('.microsoft.com') && /\/store(\/|$)/i.test(url.pathname)) return true;
    return false;
  } catch (_) {
    return false;
  }
}

// 统一推送「仅商店分发」结果（避免四处重复的对象字面量）。
// storeUrl 为商店页 URL（路径 1/2/3）；storeNote 为人工说明（路径 0 的 STORE_ONLY_VENDORS）。
function pushStoreOnly(results, entryUrl, source, storeUrl, storeNote) {
  results.push({
    entryUrl,
    source,
    error: 'STORE_ONLY',
    storeUrl: storeUrl || null,
    storeNote: storeNote || null,
    candidates: [],
    recommended: null,
    strategies: {},
  });
}

// 已知「仅通过应用商店分发、无官方直接 exe」的厂商：直接给出商店指引，避免无意义探测与仿冒风险。
// 注意：网上那些同名 "XXX-Setup-x64.exe" 多为仿冒/木马（已有安全通报），绝不能写死直链去推荐。
const STORE_ONLY_VENDORS = {
  'deepseek': 'Microsoft Store（Windows）/ App Store（macOS）/ 华为·小米·OPPO·vivo·应用宝 等安卓应用市场',
  '深度求索': 'Microsoft Store（Windows）/ App Store（macOS）/ 华为·小米·OPPO·vivo·应用宝 等安卓应用市场',
};

// ===================== 工具函数 =====================

// 解析命令行参数
function parseArgs(argv) {
  const entries = [];
  const flags = { json: false, noDownloadCheck: false, proxy: null };
  for (const a of argv) {
    if (a === '--json') flags.json = true;
    else if (a === '--no-download-check') flags.noDownloadCheck = true;
    else if (a.startsWith('--proxy=')) flags.proxy = a.slice('--proxy='.length);
    else entries.push(a);
  }
  return { entries, flags };
}

// 把参数归一化为入口 URL（厂商名走映射，否则当 URL 用）
function resolveEntry(arg) {
  if (/^https?:\/\//i.test(arg)) return arg;
  const key = arg.toLowerCase();
  if (VENDOR_MAP[key]) return VENDOR_MAP[key];
  return arg; // 兜底：原样交给 Playwright，可能失败但优雅降级
}

// 全名搜索：当输入既不是 http(s) URL、也没命中内置别名表时，用 Bing 中文搜索定位官网下载页。
// 策略：优先取结果中含 "下载"/"download" 文本或 URL 含 download/exe 的自然结果；
// 若找不到再取首个非广告、非应用商店页的结果；若搜到的全是应用商店页，返回 {storeOnly:true, url} 以便调用方明确提示；
// 均失败返回 null，由调用方优雅降级。
async function searchVendorPage(name, page) {
  const q = encodeURIComponent(name + ' 官方下载 exe');
  await page.goto('https://www.bing.com/search?q=' + q, { waitUntil: 'domcontentloaded', timeout: 30000 });
  await page.waitForSelector('#b_results', { timeout: 15000 }).catch(() => {});

  // 收集所有自然结果链接（按出现顺序），交给 Node 端做商店页过滤与打分
  const links = await page.evaluate(() => {
    const blocks = document.querySelectorAll('#b_results .b_algo');
    const out = [];
    const isJunk = (u) => /\.(js|css|png|jpg|jpeg|gif|webp|svg|ico)(\?|$)/i.test(u);
    for (const blk of blocks) {
      if (blk.closest && blk.closest('.b_ad')) continue;
      const a = blk.querySelector('h2 a');
      if (!a || !a.href || isJunk(a.href)) continue;
      out.push({ href: a.href, title: (a.textContent || '').toLowerCase() });
    }
    return out;
  }).catch(() => []);

  if (!links.length) return null;

  // 第一轮：优先「看起来像下载页」且非应用商店/非错站的结果
  for (const l of links) {
    if (isAppStoreUrl(l.href) || isWrongSiteUrl(l.href)) continue;
    if (/下载|download|\.exe/i.test(l.title + ' ' + l.href.toLowerCase())) {
      if (/^https?:\/\//i.test(l.href)) return l.href;
    }
  }
  // 第二轮：首个非应用商店、非错站、合法结果
  for (const l of links) {
    if (isAppStoreUrl(l.href) || isWrongSiteUrl(l.href)) continue;
    if (/^https?:\/\//i.test(l.href)) return l.href;
  }
  // 搜到的全是应用商店/市场页：明确标记「仅商店分发」，避免探针落到商店页空跑
  const storeHit = links.find((l) => isAppStoreUrl(l.href) && /^https?:\/\//i.test(l.href));
  if (storeHit) return { storeOnly: true, url: storeHit.href };
  return null;
}

// 分类候选链接：架构 / 是否 exe / 是否明显旧版
function classify(rawUrl) {
  const url = rawUrl.split('#')[0];
  const lower = url.toLowerCase();
  const fn = decodeURIComponent(url.split('?')[0].split('/').pop());
  const isExe = /\.exe(\?|$)/i.test(url);
  const isWindowsExe = isExe; // x64/x86 仅在 Windows exe 语境下判定，避免把 Linux .deb(amd64) 误判
  const isX64 = isWindowsExe && /x64|x86[_-]?64|win64|amd64|64bit/i.test(lower) && !/no-?x64/i.test(lower);
  const isArm64 = isWindowsExe && /arm64|aarch64/i.test(lower);
  const isX86 = isWindowsExe && /(^|[._-])x86([._-]|$)|win32|ia32|32bit/i.test(lower) && !isX64;
  // 明显旧版文件名黑名单（保守，避免误伤）
  const denylist = [/PCQQ9\.7\.25/i, /-old[._-]/i, /_old[._-]/i, /old\b(?:version)?/i];
  const denylisted = denylist.some(r => r.test(url) || r.test(fn));
  return { isExe, isX64, isArm64, isX86, denylisted };
}

// 轻量 ranged GET：取 bytes=0-1023，手动跟随 302，验证 2xx + 二进制内容类型
function verifyExe(rawUrl, depth = 0) {
  return new Promise((resolve) => {
    if (depth > MAX_REDIRECT) return resolve({ url: rawUrl, status: 'TOO_MANY_REDIRECTS', verified: false, redirects: depth });
    // 仅接受 http(s) 绝对地址，避免裸文件名/相对路径导致同步抛错
    if (!/^https?:\/\//i.test(rawUrl)) return resolve({ url: rawUrl, status: 'INVALID_URL', verified: false, redirects: depth });
    let url = rawUrl;
    const lib = url.startsWith('https') ? https : http;
    let req;
    try {
      req = lib.request(url, {
      method: 'GET',
      headers: { 'Range': 'bytes=0-1023', 'User-Agent': UA, 'Accept': '*/*' },
    }, (res) => {
      const code = res.statusCode;
      const loc = res.headers['location'];
      const ct = (res.headers['content-type'] || '').toLowerCase();
      const cr = res.headers['content-range'];
      const len = res.headers['content-length'];
      // 重定向跟随（策略 4）
      if ([301, 302, 303, 307, 308].includes(code) && loc) {
        const next = loc.startsWith('http') ? loc : new URL(loc, url).href;
        res.resume();
        return resolve(verifyExe(next, depth + 1));
      }
      // 读取少量字节确认有二进制响应体
      let size = 0;
      res.on('data', (d) => { size += d.length; });
      res.on('end', () => {
        const verified = (code >= 200 && code < 300) && (EXE_BIN_CT.test(ct) || size > 0);
        resolve({
          url,
          finalUrl: url,
          status: code,
          ct,
          verified,
          redirects: depth,
          sizeHint: cr ? cr.split('/').pop() : (len || (size ? String(size) : '?')),
        });
      });
      res.resume();
    });
    req.setTimeout(VERIFY_TIMEOUT, () => { req.destroy(); resolve({ url: rawUrl, status: 'TIMEOUT', verified: false, redirects: depth }); });
    req.on('error', (e) => resolve({ url: rawUrl, status: 'ERR:' + e.message, verified: false, redirects: depth }));
    req.end();
    } catch (e) {
      return resolve({ url: rawUrl, status: 'ERR:' + e.message, verified: false, redirects: depth });
    }
  });
}

// 轻量 HTTP GET 获取文本响应体（自动跟随 3xx 重定向），用于「无浏览器快速路径」。
// 强制 identity 编码避免 gzip 解压负担；限制体积上限（5MB）以防异常大响应拖慢。
// 若入口 URL 本身就是二进制（如用户给了一个直链），返回 isBinary=true，避免把 400MB exe 当 UTF-8 读进内存。
function httpGetText(url, { timeout = 10000, maxRedirect = MAX_REDIRECT, depth = 0 } = {}) {
  return new Promise((resolve) => {
    if (depth > maxRedirect || !/^https?:\/\//i.test(url)) {
      return resolve({ ok: false, status: 'BAD_URL', finalUrl: url, body: '' });
    }
    const lib = url.startsWith('https') ? https : http;
    const headers = { 'User-Agent': UA, 'Accept': '*/*', 'Accept-Encoding': 'identity' };
    let req;
    try {
      req = lib.request(url, { method: 'GET', headers }, (res) => {
        const code = res.statusCode;
        const loc = res.headers['location'];
        if ([301, 302, 303, 307, 308].includes(code) && loc) {
          const next = loc.startsWith('http') ? loc : new URL(loc, url).href;
          res.resume();
          return resolve(httpGetText(next, { timeout, maxRedirect, depth: depth + 1 }));
        }
        const ct = (res.headers['content-type'] || '').toLowerCase();
        const isBinary = EXE_BIN_CT.test(ct) || /\.exe(\?|$)/i.test(url);
        if (isBinary) {
          res.resume();
          return resolve({ ok: (code >= 200 && code < 300), status: code, finalUrl: url, body: '', isBinary: true });
        }
        let data = '';
        let aborted = false;
        res.setEncoding('utf8');
        res.on('data', (d) => {
          if (aborted) return;
          if (data.length > 5 * 1024 * 1024) { aborted = true; res.destroy(); return; }
          data += d;
        });
        res.on('end', () => resolve({ ok: (code >= 200 && code < 300), status: code, finalUrl: url, body: data }));
        res.on('error', () => resolve({ ok: false, status: 'READ_ERR', finalUrl: url, body: '' }));
      });
      req.setTimeout(timeout, () => { req.destroy(); resolve({ ok: false, status: 'TIMEOUT', finalUrl: url, body: '' }); });
      req.on('error', (e) => resolve({ ok: false, status: 'ERR:' + e.message, finalUrl: url, body: '' }));
      req.end();
    } catch (e) {
      resolve({ ok: false, status: 'ERR:' + e.message, finalUrl: url, body: '' });
    }
  });
}

// ===================== 官方域名信任判定（防御搜索引擎仿冒直链） =====================
// 候选直链必须落在「入口官网同域/子域」或「身份强绑定的全局信任 CDN」内，才视为官方域名。
// 仅对 source==='search'（搜索引擎定位、可能仿冒）且非官方域名的候选标记 lowTrust：
// 这类候选不会被推荐为 ★，且 C# UI 会要求复制/保存前二次确认。
// 说明：GitHub 上并不存在单一可信的“软件官方下载域名白名单”；权威数据源（winget/chocolatey/scoop）
// 是“逐包”的安装器 URL，不适合作为扁平域名表内置。这里改用「按入口官网派生白名单」，
// 自维护、零外部依赖，直接封堵仿冒域名被推荐为官方直链的风险。
const GLOBAL_TRUSTED_CDN = new Set([
  'github.com', 'githubusercontent.com', // GitHub Releases / raw，二级域名身份强绑定，难以被冒用为任意子域
  'bytegoofy.com',                        // 字节 Coze 官方 CDN（lf3-cdn-tos.bytegoofy.com），身份强绑定
  'qq.com',                               // 腾讯官方 CDN（dldir1v6.qq.com 等），注册域即 qq.com，微信/QQ 直链可信
]);

function hostOf(u) {
  try { return new URL(u).host.toLowerCase(); } catch (_) { return ''; }
}

// 归一化到「注册域」（去掉 www. 前缀，并取 eTLD+1），让 www.coze.cn 与 coze.cn 视为同一官方站点，
// 避免把官网自身的裸域/子域直链误判为低信任。
function registrable(host) {
  if (!host) return '';
  if (host.startsWith('www.')) host = host.slice(4);
  const parts = host.split('.');
  if (parts.length <= 2) return host;             // 已是 eTLD+1 或 IP/localhost
  return parts.slice(-2).join('.');               // 取最后两段（多段国别域如 .co.uk 略宽松，足够区分仿冒）
}

function classifyTrust(candUrl, entryUrl, source) {
  const ch = hostOf(candUrl);
  if (!ch) return { officialDomain: false, lowTrust: false };
  const eh = hostOf(entryUrl);
  const cr = registrable(ch), er = registrable(eh);
  let official = !!er && (cr === er || ch.endsWith('.' + eh) || ch === eh);
  if (!official && GLOBAL_TRUSTED_CDN.has(registrable(ch))) official = true;
  const lowTrust = source === 'search' && !official;
  return { officialDomain: official, lowTrust };
}

// 在已知 source 后：为每个候选打信任标记，并重新计算推荐（排除 lowTrust / denylisted）。
// 推荐逻辑从 finalizeResult 迁移至此，是因为信任判定依赖 source（搜索来源才需严格校验）。
function applyTrust(r) {
  const cands = r.candidates || [];
  for (const c of cands) {
    const t = classifyTrust(c.url, r.entryUrl, r.source);
    c.officialDomain = t.officialDomain;
    c.lowTrust = t.lowTrust;
  }
  const eligible = cands
    .filter((s) => !s.denylisted && !s.lowTrust && s.score > 0 && (s.verified || s.isX64))
    .sort((a, b) => b.score - a.score);
  r.recommended = eligible.length
    ? { url: eligible[0].url, strategy: eligible[0].strategy, isX64: eligible[0].isX64, verified: eligible[0].verified }
    : null;
}

// 把 found(Map<url,{url,strategy}>) 整理成标准结果：并行验证 + 推荐打分 + 策略统计。
// Chromium 路径与无浏览器快速路径共用，避免逻辑分叉导致的推荐不一致。
async function finalizeResult(entryUrl, found, opts) {
  const candUrls = [...found.keys()];
  const verifiedEntries = await Promise.all(candUrls.map(async (u) => {
    const entry = found.get(u);
    const cl = classify(u);
    const shouldVerify = cl.isExe || /file_redirect\.fcg/i.test(u);
    const v = shouldVerify
      ? await verifyExe(u)
      : { url: u, verified: false, status: 'SKIP', ct: '', redirects: 0, finalUrl: u };
    if (v.redirects > 0 && /(\.exe)(\?|$)/i.test(v.finalUrl || u)) {
      entry.strategy = (entry.strategy || '') + '+redirect';
    }
    return {
      url: u,
      strategy: entry.strategy,
      isX64: cl.isX64,
      isArm64: cl.isArm64,
      isX86: cl.isX86,
      denylisted: cl.denylisted,
      verified: v.verified,
      status: v.status,
      ct: v.ct || '',
      redirects: v.redirects || 0,
    };
  }));

  const scored = verifiedEntries.map((c) => {
    let score = 0;
    if (c.verified) score += 3;
    if (/\.exe(\?|$)/i.test(c.url)) score += 1; // 优先 Windows 安装包
    if (c.isX64) score += 4;
    if (c.isArm64) score -= 10;
    if (c.isX86) score -= 2;
    if (c.denylisted) score -= 100;
    return { ...c, score };
  });
  scored.sort((a, b) => b.score - a.score);

  const result = {
    entryUrl,
    strategies: { anchor: [], download: [], jsonp: [], redirect: [] },
    candidates: scored,   // 含 score，便于 UI 排序与信任判定复用
    recommended: null,    // 推荐在 applyTrust（已知 source 后）中计算，排除 lowTrust / denylisted
    error: null,
    fastPath: false,
  };

  for (const c of result.candidates) {
    const ss = (c.strategy || '').split('+');
    if (ss.includes('anchor') || ss.includes('response')) result.strategies.anchor.push(c.url);
    if (ss.includes('download')) result.strategies.download.push(c.url);
    if (ss.includes('jsonp')) result.strategies.jsonp.push(c.url);
    if (ss.includes('redirect')) result.strategies.redirect.push(c.url);
  }
  result.strategies.anchor = [...new Set(result.strategies.anchor)];
  result.strategies.download = [...new Set(result.strategies.download)];
  result.strategies.jsonp = [...new Set(result.strategies.jsonp)];
  result.strategies.redirect = [...new Set(result.strategies.redirect)];

  return result;
}

// 无浏览器快速路径：直接 HTTP 抓取入口 HTML / JSONP，扫描直链并验证。
// 命中则返回结果（跳过 Chromium 渲染，省 3-8s 启动 + 2.5s 等待 + 按钮点击）；
// 未命中（无候选 / 无真 exe 验证）返回 null，由调用方回退 Chromium。
async function probeSiteFast(entryUrl, opts) {
  try {
    const got = await httpGetText(entryUrl, { timeout: 8000 });
    if (!got.ok) return null;

    const found = new Map();
    const add = (url, strategy) => {
      if (!url) return;
      const norm = url.split('#')[0];
      if (!found.has(norm)) found.set(norm, { url: norm, strategy });
      else if (!found.get(norm).strategy.includes(strategy)) {
        found.get(norm).strategy = found.get(norm).strategy + '+' + strategy;
      }
    };

    // 入口本身就是二进制 exe（例如用户直接给了直链，或 VENDOR_MAP 中配置了官方 CDN 直链）
    if (got.isBinary && /\.exe(\?|$)/i.test(entryUrl)) {
      add(entryUrl, 'anchor');
      const result = await finalizeResult(entryUrl, found, opts);
      const good = result.candidates.some((c) => !c.denylisted && c.verified);
      if (good) {
        result.fastPath = true;
        return result;
      }
      return null;
    }

    if ((got.body || '').length < 50) return null;

    const exes = got.body.match(EXE_URL_RE) || [];
    exes.forEach((u) => add(u, 'anchor'));
    const fcgs = got.body.match(FILE_REDIRECT_RE) || [];
    fcgs.forEach((u) => add(u, 'jsonp'));

    if (found.size === 0) return null;

    const result = await finalizeResult(entryUrl, found, opts);
    // 判定快速路径是否足够好：至少有一个非旧版、且验证为「真 exe」的候选
    const good = result.candidates.some((c) => !c.denylisted && c.verified);
    if (!good) return null;

    result.fastPath = true;
    return result;
  } catch (_) {
    return null;
  }
}

// ===================== 单站探测（Chromium 兜底路径） =====================
async function probeSite(browser, entryUrl, opts) {
  const result = {
    entryUrl,
    strategies: { anchor: [], download: [], jsonp: [], redirect: [] },
    candidates: [],
    recommended: null,
    error: null,
  };

  const ctx = await browser.newContext({ userAgent: UA });
  const page = await ctx.newPage();

  // 拦截无关资源，加速页面加载：仅拦截图片/CSS/字体。
  // ⚠️ 关键：绝不能 abort 「media / other」类请求——现代下载页（如 Coze）的安装包直链往往就是
  // media/other 类型，一旦 abort，后续的 page.on('download') 与 response 二进制监听都拿不到直链，导致 0 候选。
  await page.route('**/*', (route) => {
    const rt = route.request().resourceType();
    const abortTypes = ['image', 'stylesheet', 'font'];
    if (abortTypes.includes(rt)) {
      route.abort('aborted');
    } else {
      route.continue();
    }
  });

  // 收集容器：key=url, value={url, strategy}
  const found = new Map();
  const add = (url, strategy) => {
    if (!url) return;
    if (MOBILE_MAC_PKG_RE.test(url)) return; // 跳过手机/macOS 安装包，本工具面向 Windows
    const norm = url.split('#')[0];
    if (!found.has(norm)) found.set(norm, { url: norm, strategy });
    else if (!found.get(norm).strategy.includes(strategy)) {
      found.get(norm).strategy = found.get(norm).strategy + '+' + strategy;
    }
  };

  // 诊断采集容器（点击阶段填充，最终挂到返回的 finalResult 上，便于仍 0 候选时定位 Coze 等站点）
  const dbgNetwork = new Set();   // 疑似安装包的网络请求（exe/msi/zip + download/setup/client/install 路径）
  const dbgRequests = [];         // 点击阶段全部请求（url + 资源类型），截断上限避免体积爆炸
  let dbgDlCount = 0;             // download 事件触发次数
  let dbgTriggers = 0;            // 命中的可点击触发元素数（用于诊断点击策略是否生效）
  let dbgClicks = 0;              // 实际执行的点击数

  // JSONP / 入口响应体收集（用于策略 3）
  const jsonpBodies = [];
  const isJsonpEntry = /download\.js|format=json|jsonpcallback|\.jsonp|config\.js/i.test(entryUrl);

  // 监听：response 中出现 exe 直链 / 二进制 / Windows 架构路径（策略补充）
  page.on('response', async (resp) => {
    const u = resp.url();
    const ct = (resp.headers()['content-type'] || '').toLowerCase();
    const looksLikeInstaller = /\.exe(\?|$)/i.test(u)
      || /\/(download|setup|client|install(er)?)(\b|\?|\.)/i.test(u)
      || /\b(win32|win64|x64|x86_64|amd64)\b/i.test(u);
    if (looksLikeInstaller || ct.includes('x-msdownload') || ct.includes('octet-stream')) {
      add(u, 'response');
    }
    // 抓取入口本身的 JSONP 响应体
    if (u === entryUrl || /\.js(\?|$)/i.test(u)) {
      try {
        const txt = await resp.text();
        if (txt && (FILE_REDIRECT_RE.test(txt) || EXE_URL_RE.test(txt) || /qqmusic|sign=|dldir|exe/i.test(txt))) {
          jsonpBodies.push(txt);
        }
      } catch (_) { /* 流式/已消费，忽略 */ }
    }
  });

  // 监听：download 事件（策略 2）——捕获直链后取消落盘
  if (!opts.noDownloadCheck) {
    dbgDlCount = 0;
    page.on('download', async (dl) => {
      try {
        dbgDlCount++;
        const u = dl.url();
        add(u, 'download'); // 仅记录真实直链 URL，不记录裸文件名（避免被当 URL 验证）
        try { await dl.cancel(); } catch (_) {}
      } catch (_) {}
    });
  }

  try {
    await page.goto(entryUrl, { waitUntil: 'domcontentloaded', timeout: PER_SITE_TIMEOUT });
    // 动态渲染等待：常规模式 2.5s；勾选「跳过点击下载检测」时进一步缩短到 1.2s
    await page.waitForTimeout(opts.noDownloadCheck ? 1200 : 2500);
    // SPA（如 Coze）在 domcontentloaded 后仍在 hydration / 拉取配置，额外尝试 networkidle 最多 5s。
    // 若网络持续有活动（埋点/心跳），5s 超时后自动继续，不阻断后续策略。
    try {
      await page.waitForLoadState('networkidle', { timeout: 5000 });
    } catch (_) {}

    // —— 策略 1：HTML 静态锚点 + 整页正则扫描 ——
    const anchors = await page.$$eval('a[href]', (els) =>
      els.map((e) => e.href).filter((h) => /\.exe/i.test(h))
    ).catch(() => []);
    anchors.forEach((a) => add(a, 'anchor'));

    const domExes = await page.evaluate(() => {
      const hay = document.documentElement.outerHTML;
      const m = hay.match(/https?:\/\/[^"'\\s<>()]+?\.exe[^"'\\s<>()]*/gi) || [];
      return [...new Set(m)];
    }).catch(() => []);
    domExes.forEach((a) => add(a, 'anchor'));

    // —— 策略 2：点击下载类按钮，等待 download 事件 ——
    // 针对两级 hover 菜单（如 Coze「下载桌面端」hover 出 Windows/Mac 选项）的稳健点击：
    // 先 hover 所有触发元素让悬浮子菜单挂载/显形，再重新抓取并点击（Windows 选项优先），
    // 避免先点父菜单导致页面跳转、子菜单来不及触发下载。每遍后重新扫描 DOM 兜底。
    if (!opts.noDownloadCheck) {
      // 「下载/桌面端/Windows/移动端」等按钮都会命中 triggerRe，但本工具只面向 Windows，
      // 必须跳过移动端(mobile/android/ios/apk/ipa) 与 macOS(dmg/pkg/app/deb/rpm) 按钮，
      // 否则会像上一轮那样点击到「移动端」菜单、抓到 APK 直链。
      const triggerRe = /下载|桌面端|立即下载|download|download now|windows|windows版|win版/i;
      const winRe = /windows|windows版|win版|\bwin\b|macos|\bmac\b/i; // Windows/macOS 排前点击，均属桌面端
      const skipRe = /移动端|mobile|android|ios|\bapk\b|\bipa\b/i;     // 命中则跳过（去抓 APK 就废了）
      // 监听点击过程中触发的网络请求：很多现代下载页（如 Coze）通过 JS 调接口/跳转拿到直链，
      // 该 URL 可能只在网络层出现且不一定带 .exe 后缀（如 /api/client/download?os=windows 或 /win32-x64/xxx.exe）。
      const DLISH_RE = /\.(exe|msi|zip|dmg|apk)(\?|$)/i;
      const DLISH_PATH_RE = /\/(download|setup|client|install(er)?)(\b|\?|\.)/i;
      const WIN_ARCH_RE = /\b(win32|win64|x64|x86_64|amd64|windows)\b/i;
      const onRequest = (req) => {
        const u = req.url();
        const rt = req.resourceType();
        // 全量记录点击阶段请求（截断上限），便于仍 0 候选时看清 Coze 到底有没有触发下载请求
        if (dbgRequests.length < 120) dbgRequests.push({ url: u, rt });
        if (DLISH_RE.test(u) || DLISH_PATH_RE.test(u) || WIN_ARCH_RE.test(u)) {
          dbgNetwork.add(u);
        }
      };
      page.on('request', onRequest);

      const reScanExe = async () => {
        const a = await page.$$eval('a[href]', (els) =>
          els.map((e) => e.href).filter((h) => /\.exe/i.test(h))
        ).catch(() => []);
        a.forEach((x) => add(x, 'anchor'));
        const d = await page.evaluate(() => {
          const m = document.documentElement.outerHTML.match(/https?:\/\/[^"'\\s<>()]+?\.exe[^"'\\s<>()]*/gi) || [];
          return [...new Set(m)];
        }).catch(() => []);
        d.forEach((x) => add(x, 'anchor'));
      };

      // 通用元素发现：不仅限于 a/button，也包含 div/span/label/li 等常见 SPA 自定义按钮。
      // 同时读取 aria-label / title，解决图标按钮无可见文字、或有提示文字的情况。
      const collectMatches = async () => {
        const sel = 'a, button, [role="button"], [role="link"], div, span, label, li';
        const els = await page.$$(sel).catch(() => []);
        const matches = [];
        for (const b of els) {
          const tk = (await b.innerText().catch(() => '')) || '';
          const aria = (await b.getAttribute('aria-label').catch(() => '')) || '';
          const title = (await b.getAttribute('title').catch(() => '')) || '';
          const hk = (await b.getAttribute('href').catch(() => '')) || '';
          const sig = `${tk} ${aria} ${title} ${hk}`.trim();
          if (triggerRe.test(sig) && !skipRe.test(sig)) matches.push({ b, txt: sig });
        }
        return matches;
      };

      const clickMatching = async () => {
        // 第一步：hover 所有触发元素，让悬浮子菜单（Windows/Mac 选项）显形
        const triggers = await collectMatches();
        for (const m of triggers) {
          dbgTriggers++;
          await m.b.hover().catch(() => {});
          await page.waitForTimeout(200);
        }
        // 第二步：重新抓取（含已显形的子菜单项），Windows/macOS 选项排到最前点击
        const matches = await collectMatches();
        matches.sort((x, y) => (winRe.test(x.txt) ? -1 : 0) - (winRe.test(y.txt) ? -1 : 0));
        let n = 0;
        for (const m of matches) {
          if (n >= CLICK_STRATEGY_MAX_PASS_CLICKS * 2) break;
          n++;
          dbgClicks++;
          try {
            await Promise.race([
              m.b.click({ timeout: CLICK_TIMEOUT_MS, force: true }).catch(() => {}),
              page.waitForTimeout(CLICK_TIMEOUT_MS),
            ]);
            // 点击后给 JS/Fetch 留足时间：先固定等待，再尝试 networkidle 1.5s。
            await page.waitForTimeout(POST_CLICK_SETTLE_MS);
            try { await page.waitForLoadState('networkidle', { timeout: 1500 }); } catch (_) {}
          } catch (_) {}
        }
      };

      // 两遍点击 + 每遍后重新扫描 DOM；应对点击后才出现直链 / 两级菜单
      await clickMatching();
      await reScanExe();
      await clickMatching();
      await reScanExe();
      page.off('request', onRequest);
      dbgNetwork.forEach((u) => add(u, 'network'));

      // 诊断信息：改进的点击策略必然打印一行，便于确认新代码已加载并看出 Coze 触发了什么
      process.stderr.write(`   诊断[改进点击策略]: 命中触发元素 ${dbgTriggers} 个，执行点击 ${dbgClicks} 次，网络层疑似下载请求 ${dbgNetwork.size} 个，download 事件 ${dbgDlCount} 次，点击阶段共观察请求 ${dbgRequests.length} 个\n`);

      // 也等待一个自然触发的 download 事件
      await page.waitForEvent('download', { timeout: DOWNLOAD_EVENT_TIMEOUT_MS }).catch(() => {});
    }

    // —— 策略 3：解析 JSONP / 入口响应体中的直链 ——
    if (isJsonpEntry || jsonpBodies.length) {
      const allText = jsonpBodies.join('\n');
      const fcg = allText.match(FILE_REDIRECT_RE) || [];
      fcg.forEach((u) => add(u, 'jsonp'));
      const exes = allText.match(EXE_URL_RE) || [];
      exes.forEach((u) => add(u, 'jsonp'));
    }

    // —— 策略 4：最终兜底——从完整渲染后的页面提取所有潜在安装包 URL ——
    // 适用于点击后 JS 把直链写进 data-* 属性 / script 配置 / 临时 DOM 的 SPA（如 Coze）。
    const deepUrls = await page.evaluate(() => {
      const set = new Set();
      const push = (u) => { if (u && /^https?:\/\//i.test(u)) set.add(u.split('#')[0]); };
      // 1) 所有链接与 src 属性
      document.querySelectorAll('[href],[src]').forEach((el) => {
        push(el.href); push(el.src);
      });
      // 2) data-* 常见下载相关属性
      document.querySelectorAll('*').forEach((el) => {
        for (const attr of el.attributes) {
          if (/^data-(href|url|src|download|file|link)$/i.test(attr.name)) push(attr.value);
        }
      });
      // 3) script / json 配置里的 URL
      document.querySelectorAll('script').forEach((s) => {
        const txt = s.textContent || '';
        const m = txt.match(/https?:\/\/[^\s"'<>()\\]+/gi) || [];
        m.forEach(push);
      });
      return [...set];
    }).catch(() => []);
    deepUrls.forEach((u) => {
      if (/\.(exe|msi|zip)(\?|$)/i.test(u)) add(u, 'anchor');
      if (/\/(download|setup|client|install(er)?)(\b|\?|\.)/i.test(u)) add(u, 'network');
      if (/\b(win32|win64|x64|x86_64|amd64)\b/i.test(u) && /\.(exe|msi|zip)(\?|$)/i.test(u)) add(u, 'anchor');
    });
  } catch (e) {
    result.error = e.message;
  }

  await ctx.close().catch(() => {});

  // —— 整理候选 + 验证 + 推荐（策略 4 重定向在 verifyExe 内完成）——
  // 复用 finalizeResult，与无浏览器快速路径保持完全一致的逻辑，避免推荐口径分叉。
  const finalResult = await finalizeResult(entryUrl, found, opts);
  if (result.error) finalResult.error = result.error; // 页面导航/read 异常仍透传
  // 挂载诊断数据到返回对象（挂 finalResult，而非被丢弃的本地 result），仍 0 候选时用于定位
  finalResult.networkCaptured = [...dbgNetwork];
  finalResult.debugRequests = dbgRequests.slice(0, 80);
  finalResult.dbgTriggers = dbgTriggers; // 命中触发元素数（诊断点击策略是否生效）
  finalResult.dbgClicks = dbgClicks;     // 实际点击数
  finalResult.probeVersion = '2026.08.08';
  process.stderr.write(`   探针版本: ${finalResult.probeVersion}（含诊断数据，详见 JSON 的 networkCaptured/debugRequests）\n`);
  return finalResult;
}

// ===================== 入口 =====================
async function main() {
  const { entries, flags } = parseArgs(process.argv.slice(2));
  if (!entries.length) {
    console.error('用法: node official_exe_finder.js <入口URL或厂商名> [更多...] [--json] [--proxy=http://127.0.0.1:26561]');
    process.exit(2);
  }

  // 代理：--proxy 优先；否则沿用环境 HTTPS_PROXY（本沙箱经 Watt Toolkit）
  const proxy = flags.proxy || process.env.HTTPS_PROXY || process.env.HTTP_PROXY || null;
  const launchOpts = { headless: true, args: ['--no-sandbox', '--disable-setuid-sandbox'] };
  if (proxy) launchOpts.proxy = { server: proxy };

  const results = [];

  // Chromium 懒启动：仅当某个入口确实需要时才启动（全名搜索 / 快速路径回落）。
  // 纯静态 HTML / JSONP 入口走无浏览器快速路径即可，完全不启动 Chromium，省 3-8s 启动 + 关闭耗时。
  let _browser = null;
  let _searchCtx = null;
  let _searchPage = null;
  async function getBrowser() {
    if (_browser) return _browser;
    _browser = await chromium.launch(launchOpts);
    return _browser;
  }
  async function getSearchPage() {
    if (_searchPage) return _searchPage;
    try {
      const b = await getBrowser();
      _searchCtx = await b.newContext({ userAgent: UA });
      _searchPage = await _searchCtx.newPage();
    } catch (e) {
      process.stderr.write('[!] 搜索页初始化失败，全名搜索将不可用: ' + e.message + '\n');
    }
    return _searchPage;
  }

  for (const arg of entries) {
    let entryUrl;
    let source = 'url';
    let vendorKey = null;   // 当前入口对应的厂商（canonical），用于白名单约束与 CDN 兜底

    // 路径 0：已知「仅商店分发、无官方直接 exe」的厂商 —— 直接给出商店指引，避免无意义探测与仿冒风险。
    const storeMsg = STORE_ONLY_VENDORS[arg.toLowerCase()];
    if (storeMsg) {
      process.stderr.write(`\n>>> 「${arg}」仅通过应用商店分发，无直接 exe 直链，跳过探测。\n`);
      pushStoreOnly(results, arg, 'vendor', null, storeMsg);
      continue;
    }

    if (/^https?:\/\//i.test(arg)) {
      // 路径 1：URL 直抓（保持现状，零改动）
      entryUrl = arg;
      source = 'url';
      // 直接给了一个应用商店链接：无直接 exe，明确提示而非空跑探测
      if (isAppStoreUrl(entryUrl)) {
        process.stderr.write('>>> 入口为应用商店页，无直接 exe 直链，跳过探测。\n');
        pushStoreOnly(results, entryUrl, 'url', entryUrl, null);
        continue;
      }
    } else if (VENDOR_MAP[arg.toLowerCase()]) {
      // 路径 2：内置别名映射（精确命中）
      entryUrl = VENDOR_MAP[arg.toLowerCase()];
      source = 'vendor';
      vendorKey = VENDOR_CANON[arg.toLowerCase()] || null;
      // 别名若指向应用商店页（理论上不应出现，防御性处理）
      if (isAppStoreUrl(entryUrl)) {
        pushStoreOnly(results, entryUrl, 'vendor', entryUrl, null);
        continue;
      }
    } else if ((vendorKey = inferVendorKey(arg))) {
      // 路径 2.5：未精确命中别名，但能 fuzzy 推断为已知厂商（如「微信电脑版」「coze 桌面端」）
      // → 直接采用该厂商官方入口，跳过不可靠搜索引擎，从根本上避免落到 weiyun.com 等错站。
      // 这是「官网白名单」策略的关键落点：白名单可靠时，搜索只在官网域抓直链。
      const alias = Object.keys(VENDOR_MAP).find((k) => (VENDOR_CANON[k] || k) === vendorKey);
      entryUrl = VENDOR_MAP[alias];
      source = 'vendor';
      process.stderr.write(`\n>>> 由「${arg}」推断为厂商「${vendorKey}」，直接采用官方入口（跳过搜索，避免错站）。\n`);
      if (isAppStoreUrl(entryUrl)) {
        pushStoreOnly(results, entryUrl, 'vendor', entryUrl, null);
        continue;
      }
    } else {
      // 路径 3：全名搜索——用 Bing 中文搜索定位官网，再走现有 4 种抓取策略
      process.stderr.write(`\n>>> 未命中别名表，尝试通过搜索引擎定位「${arg}」的官网…\n`);
      let found = null;
      try {
        const sp = await getSearchPage();
        found = sp ? await searchVendorPage(arg, sp) : null;
      } catch (e) {
        process.stderr.write('   搜索异常: ' + e.message + '\n');
      }
      if (typeof found === 'string') {
        // 该提示写入 stderr（--json 模式下 stdout 必须是纯 JSON，不能污染）。
        // C# 侧日志会显示此行，便于用户感知定位过程。
        process.stderr.write('>>> 通过搜索定位到官网: ' + found + '\n');
        entryUrl = found;
        source = 'search';
      } else if (found && found.storeOnly) {
        process.stderr.write('>>> 搜索结果均为应用商店页（无直接 exe 直链）：' + found.url + '\n');
        // 优雅跳过该入口：写入「仅商店分发」标记、继续后续入口（若有），退出码保持 0
        pushStoreOnly(results, found.url, 'search', found.url, null);
        continue;
      } else {
        process.stderr.write('[!] 未找到「' + arg + '」的官网，请直接输入官网下载页 URL 重试\n');
        // 优雅跳过该入口：写入未命中标记、继续后续入口（若有），退出码保持 0、不抛未捕获异常
        results.push({ entryUrl: arg, source: 'search', error: 'NOT_FOUND', candidates: [], recommended: null, strategies: {} });
        continue;
      }
    }

    process.stderr.write(`\n>>> 探测: ${arg}  ->  ${entryUrl}  (source=${source})\n`);
    let r = null;
    // 无浏览器快速路径：静态 HTML / JSONP 下载页直接用 Node HTTP 扫描直链，跳过 Chromium 启动与渲染等待。
    // 已知需 JS 交互的站点（JS_HEAVY）及非 http(s) 入口直接跳过，避免空耗一次 HTTP 扫描。
    const isJsHeavy = JS_HEAVY.has(arg.toLowerCase());
    if (!isJsHeavy && /^https?:\/\//i.test(entryUrl)) {
      process.stderr.write('   → 尝试无浏览器快速路径（HTTP 扫描入口）…\n');
      r = await probeSiteFast(entryUrl, flags);
      if (r) {
        process.stderr.write(`   ✅ 快速路径命中 ${r.candidates.length} 个候选，推荐: ${r.recommended ? r.recommended.url : '无'}\n`);
      } else {
        process.stderr.write('   快速路径未命中（无静态直链），回退 Chromium 渲染路径…\n');
      }
    }
    if (!r && !/\.exe(\?|$)/i.test(entryUrl)) {
      // 入口本身已是直接 .exe 直链、但快速路径验证失败（如版本过期 404）时，无需再启动 Chromium 渲染
      // （无页面可抓），直接交由下方 FALLBACK_CDN 兜底逻辑处理版本漂移告警，省去一次空耗的浏览器启动。
      try {
        r = await Promise.race([
          probeSite(await getBrowser(), entryUrl, flags),
          new Promise((_, rej) => setTimeout(() => rej(new Error('PER_SITE_TIMEOUT')), PER_SITE_TIMEOUT + 5000)),
        ]);
      } catch (e) {
        r = { entryUrl, source, error: e.message, candidates: [], recommended: null, strategies: {} };
        process.stderr.write(`   失败: ${e.message}\n`);
      }
    }
    if (r) {
      r.source = source; // 附上来源标记（url/vendor/search），便于 C# 侧提示用户核对
      applyTrust(r);     // 按来源做域名信任判定并重新计算推荐（排除低信任候选）
      results.push(r);
      process.stderr.write(`   候选 ${r.candidates.length} 个，推荐: ${r.recommended ? r.recommended.url : '无'}${r.fastPath ? '  [快速路径]' : ''}\n`);
    }
    // 版本漂移兜底：主探测（官网实时抓取）未拿到可用 exe、且该厂商配置了官方 CDN 直链兜底时，
    // 用写死的 CDN 直链再做一次快速验证。这样「白名单 + 实时抓取」是主路径（无版本漂移），
    // 写死直链仅作最后的保底（Coze 这类 JS/Fetch 下发直链、Kimi 这类登录门控的站点）。
    // 若兜底直链本身也已 404（版本过期），verifyExe 返回非 2xx，明确告警而非静默失效。
    if ((!r || !r.recommended) && vendorKey && FALLBACK_CDN[vendorKey]) {
      const fb = FALLBACK_CDN[vendorKey];
      process.stderr.write(`\n>>> 主探测未获直链，尝试官方 CDN 兜底: ${fb}\n`);
      const fbRes = await probeSiteFast(fb, flags);
      if (fbRes) {
        fbRes.source = 'vendor';
        fbRes.usedFallbackCdn = true;
        applyTrust(fbRes); // 必须在判断 recommended 之前调用（probeSiteFast 自身不计算 recommended）
      }
      if (fbRes && fbRes.recommended) {
        if (r && !r.recommended) results.pop(); // 去掉主探测的空结果，避免重复
        results.push(fbRes);
        process.stderr.write(`   ✅ 兜底命中: ${fbRes.recommended.url}\n`);
      } else {
        process.stderr.write('   ⚠️ 兜底 CDN 直链验证失败（可能版本已过期 404），请在探针中更新版本号或改回官网实时抓取。\n');
        // 主探测与兜底均失败：产出带「版本漂移」告警的结果（而非静默返回空数组），
        // C# UI 可据此提示用户「官方 CDN 版本可能已过期，请在探针中更新版本号」。
        results.push({
          entryUrl: arg, source: 'vendor', vendorKey,
          error: 'CDN_FALLBACK_FAILED',
          note: '官方 CDN 直链验证失败（可能版本已过期 404），请在探针中更新版本号或改回官网实时抓取。',
          candidates: [], recommended: null, strategies: {}, usedFallbackCdn: true,
        });
      }
    }
  }

  // 关闭搜索上下文与浏览器（若从未启动则此处为空操作，纯快速路径场景零 Chromium 开销）
  if (_searchCtx) await _searchCtx.close().catch(() => {});
  if (_browser) await _browser.close().catch(() => {});

  // —— 输出 ——
  if (flags.json) {
    console.log(JSON.stringify(results, null, 2));
  } else {
    for (const r of results) {
      console.log('\n========================================');
      console.log(`入口: ${r.entryUrl}`);
      if (r.fastPath) console.log('探测路径: 无浏览器快速路径（HTTP 扫描，已跳过 Chromium）');
      if (r.error === 'STORE_ONLY') {
        const where = r.storeNote || (r.storeUrl ? '应用商店页：' + r.storeUrl : '应用商店');
        console.log('分发方式: 仅通过应用商店分发，无直接 .exe 直链');
        console.log(`商店指引: ${where}`);
        console.log('说明: 该应用官方只上架应用商店（Microsoft Store / App Store / 各大安卓市场），');
        console.log('      网上同名 "XXX-Setup-x64.exe" 多为仿冒/木马，请勿从非官方渠道下载。');
        console.log('建议: 请在对应应用商店搜索安装，或访问官网获取商店入口。');
        console.log('----------------------------------------');
        continue;
      }
      if (r.error) console.log(`探测错误: ${r.error}`);
      if (r.source === 'search') {
        console.log('⚠️ 来源: 搜索引擎自动定位（低信任），下载前请务必核对域名确为官方站点');
      }
      console.log('策略命中:');
      console.log(`  静态锚点 : ${r.strategies.anchor.length}`);
      console.log(`  download : ${r.strategies.download.length}`);
      console.log(`  JSONP    : ${r.strategies.jsonp.length}`);
      console.log(`  重定向   : ${r.strategies.redirect.length}`);
      console.log('候选直链:');
      for (const c of r.candidates) {
        const tags = [
          c.isX64 ? 'x64' : (c.isArm64 ? 'arm64' : (c.isX86 ? 'x86' : '?')),
          c.verified ? '✅真exe' : '⚠️未验证',
          c.denylisted ? '🚫旧版' : '',
          c.lowTrust ? '⚠️域名待核对' : '',
        ].filter(Boolean).join(' ');
        console.log(`  [${c.strategy}] ${c.url}`);
        console.log(`        ${tags}  (status=${c.status}, ct=${c.ct || '?'})`);
      }
      console.log('推荐直链:');
      if (r.recommended) {
        console.log(`  ${r.recommended.url}`);
        console.log(`    x64=${r.recommended.isX64} 真exe=${r.recommended.verified} 策略=${r.recommended.strategy}`);
      } else {
        console.log('  （未找到满足 x64 + 真exe 的候选）');
      }
      console.log('----------------------------------------');
    }
    console.log('\n===JSON===');
    console.log(JSON.stringify(results, null, 2));
  }
}

main().catch((e) => { console.error('FATAL', e); process.exit(1); });
