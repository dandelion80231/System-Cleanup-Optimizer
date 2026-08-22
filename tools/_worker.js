/// Cloudflare Pages Functions — advanced mode (_worker.js)
/// Runs for ALL requests on the cpq-system-tool.pages.dev domain.
/// Serves static assets via the ASSETS binding and sets per-response
/// Cache-Control + security headers. This is the long-cache fix that the
/// _headers file cannot provide on Direct Upload (Functions ignore _headers).

export default {
  async fetch(request, env) {
    const url = new URL(request.url);

    // Never expose the function source.
    if (url.pathname === "/_worker.js") {
      return new Response("Not Found", { status: 404 });
    }

    // Serve the static asset through the default ASSETS binding.
    let res = await env.ASSETS.fetch(request);

    // Pretty-URL fallback: /features -> /features.html
    if (res.status === 404 && !url.pathname.includes(".") && !url.pathname.endsWith("/")) {
      const r2 = await env.ASSETS.fetch(
        new Request(url.origin + url.pathname + ".html", request)
      );
      if (r2.status !== 404) res = r2;
    }

    // Custom 404 page for any still-missing route — serve it with a real 404
    // status (not a soft-404). The build-in /404.html asset is left as-is.
    if (res.status === 404 && url.pathname !== "/404.html") {
      const r404 = await env.ASSETS.fetch(new Request(url.origin + "/404.html", request));
      if (r404.status !== 404) {
        res = new Response(r404.body, { status: 404, headers: r404.headers });
      }
    }

    const out = new Response(res.body, res);
    const p = url.pathname;

    // Fingerprinted / long-lived assets: cache forever (immutable).
    const immutable = /\.(css|js|mjs|ico|png|jpe?g|gif|webp|svg|woff2?|ttf|eot|map|wasm|exe)$/i.test(p);
    if (immutable) {
      out.headers.set("Cache-Control", "public, max-age=31556952, immutable");
    } else {
      // HTML and everything else: keep a 5-minute long cache with
      // must-revalidate so the browser reuses the cached copy within the
      // window but rechecks with the origin on expiry. This is the correct
      // balance — the earlier "didn't take effect" reports were NOT caused
      // by this header (they were missing content sync + stale browser
      // caches); no-store was an over-correction and is reverted.
      out.headers.set("Cache-Control", "public, max-age=300, must-revalidate");
    }

    // Security / privacy headers.
    out.headers.set("X-Content-Type-Options", "nosniff");
    out.headers.set("X-Frame-Options", "DENY");
    out.headers.set("Referrer-Policy", "strict-origin-when-cross-origin");
    out.headers.set("Strict-Transport-Security", "max-age=31556952; includeSubDomains");
    out.headers.set("X-Robots-Tag", res.status >= 400 ? "noindex, follow" : "index, follow");

    // Content Security Policy (applied per-response by the Pages Function).
    // Locks scripts/styles to same-origin, blocks plugins, framing and untrusted
    // base/form targets. Inline <style> (noscript reveal fallback) is allowed;
    // the JSON-LD block is a non-executable type and is exempt from script-src.
    out.headers.set(
      "Content-Security-Policy",
      "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self'; object-src 'none'; base-uri 'self'; frame-ancestors 'none'; form-action 'self'"
    );

    return out;
  },
};
