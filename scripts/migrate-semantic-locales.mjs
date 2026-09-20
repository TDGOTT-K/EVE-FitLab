import fs from 'node:fs';
// One-time migration. The checked-in alias map is stable: never regenerate IDs
// when translations change. Existing Chinese literals are transitional aliases.
const root = new URL('../', import.meta.url),
  read = (name) => JSON.parse(fs.readFileSync(new URL(name, root), 'utf8'));
const source = read('locales/source.json'),
  en = read('locales/en.json');
const aliases = fs.existsSync(new URL('locales/legacy-aliases.json', root))
  ? read('locales/legacy-aliases.json')
  : {};
Object.assign(aliases, {
  日本語: 'language.japanese',
  '变化"': 'ui.changeAttributeSuffix',
  '行：': 'ui.lineSuffix',
});
const occupied = new Set(Object.values(aliases));
for (const [key, value] of Object.entries(source)) {
  if (/^[a-z]+(?:\.[a-zA-Z0-9]+)+$/.test(key)) continue;
  if (aliases[key]) continue;
  const words = (en[key] || '')
    .replace(/\{\w+\}/g, '')
    .replace(/[^A-Za-z0-9 ]/g, ' ')
    .trim()
    .split(/\s+/)
    .filter(Boolean)
    .slice(0, 9);
  if (!words.length) throw Error('Translate before assigning semantic identifier: ' + key);
  let id =
      'ui.' +
      words
        .map((word, i) => (i ? word[0].toUpperCase() + word.slice(1).toLowerCase() : word.toLowerCase()))
        .join(''),
    base = id,
    n = 2;
  while (occupied.has(id)) id = base + n++;
  aliases[key] = id;
  occupied.add(id);
}
for (const locale of ['source', 'en', 'ja', 'zh-TW', 'de', 'ru', 'fr']) {
  const data = read('locales/' + locale + '.json');
  if (Object.keys(data).length !== Object.keys(source).length) throw Error('Incomplete ' + locale);
  const migrated = Object.fromEntries(
    Object.entries(data).map(([key, value]) => [aliases[key] || key, value]),
  );
  fs.writeFileSync(new URL('locales/' + locale + '.json', root), JSON.stringify(migrated, null, 2) + '\n');
}
fs.writeFileSync(new URL('locales/legacy-aliases.json', root), JSON.stringify(aliases, null, 2) + '\n');
