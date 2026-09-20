import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
const storage = new Map([
  ['fitlab-language', 'en'],
  ['fitlab-working-draft', '{"name":"保存 · 保存する","shipId":587}'],
]);
globalThis.localStorage = {
  getItem: (key) => storage.get(key),
  setItem: (key, value) => storage.set(key, value),
};
globalThis.document = {
  documentElement: { lang: '' },
  body: { nodeType: 1, closest: () => false, getAttribute: () => null },
  title: 'EVE FitLab',
  createTreeWalker: () => ({ nextNode: () => null }),
};
globalThis.NodeFilter = { SHOW_ELEMENT: 1, SHOW_TEXT: 4 };
globalThis.location = { hash: '#fitting' };
globalThis.window = { dispatchEvent: () => {} };
globalThis.MutationObserver = class {
  observe() {}
  disconnect() {}
};
globalThis.fetch = async (url) => ({
  ok: true,
  json: async () => JSON.parse(await fs.readFile(new URL('.' + url, import.meta.url), 'utf8')),
});
const m = await import('./i18n.js');
await m.initI18n();
const draft = storage.get('fitlab-working-draft');
for (const locale of Object.keys(m.languages)) {
  await m.setLocale(locale);
  assert.equal(storage.get('fitlab-working-draft'), draft);
  assert.equal(
    m.t('library.deletePrompt', { name: '保存 {count} · Delete' }).includes('保存 {count} · Delete'),
    true,
    'parameters must be opaque',
  );
  assert.equal(m.gameName({ id: 999999, names: { en: 'English fallback' } }), 'English fallback');
  assert.equal(m.matchesName({ id: 1, names: { ja: 'リフター' } }, 'リフター'), true);
  assert.equal(
    m.formatNumber(1234.567, { maximumFractionDigits: 2 }),
    new Intl.NumberFormat(locale, { maximumFractionDigits: 2 }).format(1234.567),
  );
  assert.equal(m.t('unknown.internalKey').includes('unknown.internalKey'), false);
}
// Unknown engine diagnostics and protocol tokens are not success messages or translation keys.
assert.equal(m.t('OUTPUT_EXISTS'), 'OUTPUT_EXISTS');
assert.ok(m.missingTranslations().length);
console.log(
  'Seven locales: opaque parameters, draft preservation, official-name fallback, search, Intl and missing-key diagnostics passed',
);

await m.setLocale('en');
assert.equal(
  m.withPresentationLocale('de', () => m.formatNumber(1234.5)),
  '1.234,5',
);
assert.equal(m.getLocale(), 'en');
assert.equal(storage.get('fitlab-language'), 'en');
assert.throws(() =>
  m.withPresentationLocale('ja', () => {
    throw Error('fixture');
  }),
);
assert.equal(m.getLocale(), 'en');

await m.setLocale('en');assert.equal(m.t('library.itemCount',{count:1}),'1 item');assert.equal(m.t('library.itemCount',{count:2}),'2 items');await m.setLocale('ru');assert.equal(m.t('library.itemCount',{count:2}),'2 предмета');assert.equal(m.t('library.itemCount',{count:5}),'5 предметов');
