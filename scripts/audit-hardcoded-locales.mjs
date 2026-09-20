import { parse } from '@babel/parser';
import tm from '@babel/traverse';
import fs from 'node:fs';
const traverse = tm.default || tm;
const aliases = JSON.parse(fs.readFileSync('locales/legacy-aliases.json', 'utf8'));
const old = { ...JSON.parse(fs.readFileSync('locales/source.json', 'utf8')), ...aliases },
  found = {};
function add(s, file) {
  s = s.trim();
  if (!/[\u3400-\u9fff]/.test(s) || s.length > 700 || /[<>{}=\\]/.test(s) || /^['"\[\]]/.test(s)) return;
  if (!old[s]) (found[s] ??= []).push(file);
}
function pieces(s, file) {
  if (/[<>]/.test(s)) {
    for (const m of s.matchAll(/(?:title|aria-label|placeholder|alt)=["']([^"']*)["']/g)) add(m[1], file);
    for (const p of s.split(/<[^>]*>/g)) for (const q of p.split(/[<>]/)) add(q, file);
  } else add(s, file);
}
for (const file of fs
  .readdirSync('.')
  .filter((f) => f.endsWith('.js') && !/vendor|catalog|locale|test|i18n|loadout-item-info/.test(f))) {
  traverse(parse(fs.readFileSync(file, 'utf8'), { sourceType: 'module' }), {
    StringLiteral(p) {
      pieces(p.node.value, file);
    },
    TemplateElement(p) {
      pieces(p.node.value.cooked || '', file);
    },
  });
}
pieces(fs.readFileSync('index.html', 'utf8'), 'index.html');
fs.mkdirSync('output', { recursive: true });
fs.writeFileSync('output/localization-hardcoded-audit.json', JSON.stringify(found, null, 2));
console.log(
  Object.keys(found).length +
    ' candidate literals not covered by resources; review output/localization-hardcoded-audit.json (includes non-UI candidates)',
);
