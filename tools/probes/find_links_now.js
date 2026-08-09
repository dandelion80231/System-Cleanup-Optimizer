// One-off resolver to fetch CURRENT official direct links for qq / xshell / qqmusic,
// mimicking SoftwareInstall.PageLinkResolver logic, then verify each as a real .exe.
const https = require('https');
const http = require('http');
const { URL } = require('url');

const UA = 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0 Safari/537.36';

function fetchUrl(url, headers = {}, redirects = 8) {
  return new Promise((resolve, reject) => {
    const follow = (u, h, depth) => {
      if (depth > redirects) return reject(new Error('too many redirects: ' + u));
      const lib = u.startsWith('https') ? https : http;
      const req = lib.get(u, { headers: { 'User-Agent': UA, ...h } }, res => {
        const loc = res.headers.location;
        if ([301, 302, 303, 307, 308].includes(res.statusCode) && loc) {
          const next = new URL(loc, u).toString();
          res.resume();
          return follow(next, h, depth + 1);
        }
        let body = '';
        res.setEncoding('utf8');
        res.on('data', d => body += d);
        res.on('end', () => resolve({ status: res.statusCode, contentType: (res.headers['content-type'] || ''), finalUrl: u, body, contentLength: res.headers['content-length'] }));
      });
      req.on('error', reject);
      req.setTimeout(30000, () => req.destroy(new Error('timeout: ' + u)));
    };
    follow(url, headers, 0);
  });
}

function headVerify(url) {
  return new Promise((resolve) => {
    const lib = url.startsWith('https') ? https : http;
    const req = lib.request(url, { method: 'HEAD', headers: { 'User-Agent': UA } }, res => {
      resolve({ status: res.statusCode, contentType: res.headers['content-type'] || '', contentLength: res.headers['content-length'] || '', finalUrl: res.headers.location ? new URL(res.headers.location, url).toString() : url });
    });
    req.on('error', e => resolve({ status: 'ERR', error: e.message }));
    req.setTimeout(30000, () => { req.destroy(); resolve({ status: 'TIMEOUT' }); });
    req.end();
  });
}

async function main() {
  const results = {};

  // ---- QQ (QQNT x64) ----
  try {
    const r = await fetchUrl('https://im.qq.com/pcqq/');
    const re = /https?:\/\/[^\s"'<>]+\.exe/gi;
    let m, qqX64 = null, any = null;
    while ((m = re.exec(r.body)) !== null) {
      const u = m[0];
      if (u.indexOf('QQNT') >= 0 && u.indexOf('x64') >= 0 && u.indexOf('arm64') < 0) qqX64 = u;
      if (!any) any = u;
    }
    results.qq = { page: 'https://im.qq.com/pcqq/', qqX64, any };
  } catch (e) { results.qq = { error: e.message }; }

  // ---- Xshell (latest pointer) ----
  try {
    const r = await fetchUrl('https://cdn.netsarang.net/v8/Xshell-latest-p');
    results.xshell = { finalUrl: r.finalUrl, contentType: r.contentType, status: r.status };
  } catch (e) { results.xshell = { error: e.message }; }

  // ---- QQ Music (download.js JSONP config) ----
  try {
    const r = await fetchUrl('https://y.qq.com/download/download.js?cv=4747474&ct=24&format=json&inCharset=utf-8&outCharset=utf-8&notice=0&platform=yqq.json&needNewCode=1&uin=0&g_tk_new_20200303=5381&g_tk=5381&jsonpCallback=MusicJsonCallback', { 'Referer': 'https://y.qq.com/download/download.html' });
    let json = r.body;
    const lp = json.indexOf('('), rp = json.lastIndexOf(')');
    if (lp >= 0 && rp > lp) json = json.substring(lp + 1, rp - lp - 1);
    const winRe = /"Ftitle"\s*:\s*"Windows[^"]*"[\s\S]*?"Flink1"\s*:\s*"(?<u>[^"]+)"/i;
    const wm = winRe.exec(json);
    const fr = /"Flink1"\s*:\s*"(?<u>[^"]+)"/i;
    const fm = fr.exec(json);
    results.qqmusic = { winFlink: wm ? wm.groups.u : null, firstFlink: fm ? fm.groups.u : null };
  } catch (e) { results.qqmusic = { error: e.message }; }

  console.log(JSON.stringify(results, null, 2));

  // ---- Verify candidates ----
  console.log('\n=== VERIFY ===');
  const candidates = [];
  if (results.qq && results.qq.qqX64) candidates.push(['qq', results.qq.qqX64]);
  if (results.xshell && results.xshell.finalUrl) candidates.push(['xshell', results.xshell.finalUrl]);
  if (results.qqmusic && results.qqmusic.winFlink) candidates.push(['qqmusic', results.qqmusic.winFlink]);

  for (const [name, url] of candidates) {
    const v = await headVerify(url);
    console.log(name, '=>', JSON.stringify(v));
  }
}

main().catch(e => { console.error('FATAL', e); process.exit(1); });
