addEventListener('fetch', event => event.respondWith(download(event)));

async function download(event) {
  const request = event.request;
  const url = new URL(request.url);
  if (!['GET', 'HEAD'].includes(request.method)) return new Response('Method not allowed', {status: 405, headers: {Allow: 'GET, HEAD'}});
  const match = url.pathname.match(/^\/downloads\/v(\d+\.\d+\.\d+(?:-(?:rc|beta)\.\d+)?)\/EVE-FitLab-([^/]+)-Windows-x64-Setup\.exe$/);
  if (!match || match[1] !== match[2]) return new Response('Not found', {status: 404});
  const cache = caches.default;
  const key = new Request(url.origin + url.pathname, {headers: request.headers.has('Range') ? {Range: request.headers.get('Range')} : {}});
  // If-Range needs origin validation; never serve an unchecked cached range.
  if (!request.headers.has('If-Range')) {
    const hit = await cache.match(key);
    if (hit) {
      const response = new Response(request.method === 'HEAD' ? null : hit.body, hit);
      response.headers.set('X-FitLab-Cache', 'HIT');
      return response;
    }
  }
  const headers = new Headers({'User-Agent': 'EVE-FitLab-Downloads', 'Accept-Encoding': 'identity'});
  for (const name of ['Range', 'If-Range']) if (request.headers.has(name)) headers.set(name, request.headers.get(name));
  let target = 'https://github.com/TDGOTT-K/EVE-FitLab/releases/download/' + url.pathname.slice('/downloads/'.length);
  try {
    let upstream;
    for (let i = 0; i < 4; i++) {
      upstream = await fetch(target, {method: request.method, headers, redirect: 'manual', cf: {cacheEverything: true, cacheTtlByStatus: {'200-299': 86400, '300-399': 300, '400-599': -1}}});
      if (![301, 302, 303, 307, 308].includes(upstream.status)) break;
      const next = new URL(upstream.headers.get('Location'), target);
      if (next.protocol !== 'https:' || !['github.com', 'release-assets.githubusercontent.com', 'objects.githubusercontent.com'].includes(next.hostname)) throw new Error('Unexpected origin');
      await upstream.body?.cancel();
      target = next.href;
    }
    if (![200, 206, 416].includes(upstream.status)) {
      await upstream.body?.cancel();
      return new Response('Download unavailable. Please retry shortly.', {status: upstream.status === 404 ? 404 : 502, headers: {'Cache-Control': 'no-store'}});
    }
    const out = new Headers();
    for (const name of ['Content-Length', 'Content-Range', 'ETag', 'Last-Modified', 'Accept-Ranges']) if (upstream.headers.has(name)) out.set(name, upstream.headers.get(name));
    out.set('Content-Type', 'application/octet-stream');
    out.set('Content-Disposition', 'attachment; filename="' + url.pathname.split('/').pop() + '"');
    out.set('X-Content-Type-Options', 'nosniff');
    out.set('Cache-Control', upstream.status === 416 ? 'no-store' : 'public, max-age=86400');
    out.set('X-FitLab-Cache', 'MISS');
    out.set('X-FitLab-Origin-Cache', upstream.headers.get('CF-Cache-Status') || 'NONE');
    const response = new Response(upstream.body, {status: upstream.status, headers: out});
    if (request.method === 'GET' && upstream.status === 200 && !request.headers.has('Range')) {
      event.waitUntil(cache.put(new Request(url.origin + url.pathname), response.clone()).catch(error => console.error('Cache fill failed', error.message)));
    }
    return response;
  } catch {
    return new Response('Download unavailable. Please retry shortly.', {status: 502, headers: {'Cache-Control': 'no-store'}});
  }
}
