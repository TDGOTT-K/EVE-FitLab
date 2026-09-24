const test = require('node:test');
const assert = require('node:assert/strict');
const vm = require('node:vm');
const fs = require('node:fs');
const source = fs.readFileSync('infrastructure/downloads/worker.js', 'utf8');
const url = 'https://imfishman.com/downloads/v0.1.5/EVE-FitLab-0.1.5-Windows-x64-Setup.exe';
async function run(request, fetcher, cached) {
  let handler; const writes = []; const pending = [];
  vm.runInNewContext(source, {URL, Request, Response, Headers, console,
    addEventListener: (_, fn) => {handler = fn;}, fetch: fetcher,
    caches: {default: {match: async () => cached, put: async (key, response) => writes.push([key, await response.text()])}}});
  let response;
  handler({request, respondWith: value => {response = value;}, waitUntil: p => pending.push(p)});
  const result = await response;
  await Promise.all(pending);
  return {result, writes};
}
test('rejects methods and mismatched versions without origin access', async () => {
  const fail = () => {throw Error('Origin must not be called');};
  assert.equal((await run(new Request(url, {method:'POST'}), fail)).result.status, 405);
  assert.equal((await run(new Request(url.replace('/v0.1.5/', '/v0.1.4/')), fail)).result.status, 404);
});
test('streams asset, strips client credentials, follows only trusted redirects and caches full GET', async () => {
  let calls = 0;
  const {result, writes} = await run(new Request(url, {headers:{Cookie:'private', Authorization:'private'}}), async (target, options) => {
    assert.equal(options.headers.get('Cookie'), null);
    assert.equal(options.headers.get('Authorization'), null);
    if (++calls === 1) return new Response(null, {status:302, headers:{Location:'https://release-assets.githubusercontent.com/test'}});
    return new Response('MZtest', {headers:{'Content-Length':'6'}});
  });
  assert.equal(await result.text(), 'MZtest'); assert.equal(writes.length, 1);
  assert.equal(result.headers.get('Location'), null);
});
test('range forwarded and partial response never cached as full file', async () => {
  const {result,writes} = await run(new Request(url,{headers:{Range:'bytes=2-5'}}), async (_, options) => {
    assert.equal(options.headers.get('Range'),'bytes=2-5');
    return new Response('test',{status:206,headers:{'Content-Range':'bytes 2-5/6','Content-Length':'4'}});
  });
  assert.equal(result.status,206); assert.equal(writes.length,0);
});
test('HEAD cache hit returns headers without body', async () => {
  const {result}=await run(new Request(url,{method:'HEAD'}),()=>{throw Error('No origin expected');},new Response('MZtest',{headers:{'Content-Length':'6'}}));
  assert.equal(await result.text(),''); assert.equal(result.headers.get('Content-Length'),'6');
});
test('untrusted redirect fails closed without forwarding to browser', async () => {
  const {result}=await run(new Request(url),async()=>new Response(null,{status:302,headers:{Location:'https://example.com/evil'}}));
  assert.equal(result.status,502); assert.equal(result.headers.get('Location'),null);
});
test('origin errors are not cached', async () => {
  const {result,writes}=await run(new Request(url),async()=>new Response('missing',{status:404}));
  assert.equal(result.status,404); assert.equal(result.headers.get('Cache-Control'),'no-store'); assert.equal(writes.length,0);
});
