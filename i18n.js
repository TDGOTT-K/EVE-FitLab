import { toTraditional } from './locale-vendor.js';
export const languages = {
  'zh-CN': '简体中文',
  'zh-TW': '繁體中文',
  en: 'English',
  ja: '日本語',
  de: 'Deutsch',
  ru: 'Русский',
  fr: 'Français',
};
const system = () => {
  const n = globalThis.navigator?.language || 'zh-CN';
  return /^(zh-(TW|HK|MO|Hant))/i.test(n)
    ? 'zh-TW'
    : n.startsWith('zh')
      ? 'zh-CN'
      : ['ja', 'de', 'ru', 'fr'].find((code) => n.startsWith(code)) || 'en';
};
let locale;
try {
  locale = localStorage.getItem('fitlab-language');
} catch {}
locale = languages[locale] ? locale : system();
const descriptions = new Map();
const missing = new Set(),
  metricPresentations = new Map();
export const missingTranslations = () => [...missing];
let aliases = {},
  tables = {},
  game = { terms: {}, names: {} },
  phrases = [],
  pattern = null,
  revision = 0;
let presentationLocale = null;
export const getLocale = () => presentationLocale || locale;
const quote = (s) => s.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
const bundles = new Map();
function bundle(language) {
  if (bundles.has(language)) return bundles.get(language);
  const map = {};
  for (const [source, translations] of Object.entries(game.terms))
    map[source] =
      language === 'zh-TW' ? toTraditional(source) : translations[language] || translations.en || source;
  Object.assign(map, language === 'zh-CN' ? {} : tables.en || {}, tables[language] || {});
  for (const [source, key] of Object.entries(aliases))
    map[source] = tables[language]?.[key] || tables.en?.[key] || tables['zh-CN']?.[key] || source;
  const keys = Object.keys({ ...tables.en, ...tables[language], ...aliases })
    .filter((k) => k && /[\u3400-\u9fff]/.test(k))
    .sort((a, b) => b.length - a.length);
  const b = { map, pattern: keys.length ? new RegExp(keys.map(quote).join('|'), 'g') : null };
  bundles.set(language, b);
  return b;
}
function rebuild() {
  const b = locale === 'zh-CN' ? { map: {}, pattern: null } : bundle(locale);
  phrases = b.map;
  pattern = b.pattern;
  revision++;
}
export function translateFor(language, source) {
  if (source == null) return '';
  const text = String(source);
  if (tables['zh-CN']?.[text] && tables['zh-CN'][text] !== text)
    return tables[language]?.[text] || tables.en?.[text] || tables['zh-CN'][text];
  if (language === 'zh-CN') return text;
  const b = bundle(language);
  const official = game.terms[text.trim()];
  const exact = official
    ? language === 'zh-TW'
      ? toTraditional(text.trim())
      : official[language] || official.en
    : (b.map[text] ?? b.map[text.trim()]);
  let result =
    exact != null
      ? text.replace(text.trim(), () => exact)
      : b.pattern
        ? text
            .split(/(「[^」]*」)/g)
            .map((part) => (part.startsWith('「') ? part : part.replace(b.pattern, (s) => b.map[s])))
            .join('')
        : text;
  return language === 'zh-TW'
    ? result
        .split(/(「[^」]*」)/g)
        .map((part) => (part.startsWith('「') ? part : toTraditional(part)))
        .join('')
    : result;
}
export function t(source, params = {}) {
  source = aliases[source] || source;
  if(typeof params.count==='number'&&Number.isFinite(params.count)){const variant=source+'.'+new Intl.PluralRules(locale).select(params.count);if(tables['zh-CN']?.[variant])source=variant;}
  const semantic = tables['zh-CN']?.[source];
  if (!semantic && /^[a-z]+(?:\.[a-zA-Z0-9]+)+$/.test(source)) {
    missing.add(locale + ':' + source);
    return tables[locale]?.['common.translationUnavailable'] || tables.en?.['common.translationUnavailable'] || '不可用';
  }
  const template =
    semantic && source !== semantic
      ? tables[locale]?.[source] || tables.en?.[source] || semantic
      : translateFor(locale, source);
  if (semantic && source !== semantic && !tables[locale]?.[source]) missing.add(locale + ':' + source);
  return template.replace(/\{(\w+)\}/g,(m,k)=>k==='count'&&typeof params[k]==='number'?formatNumber(params[k]):params[k]??m);
}
export function gameName(type, language = locale) {
  const id = typeof type === 'object' ? type.id : type,
    n = typeof type === 'object' && type.names ? type.names : game.names[id];
  const fallback = typeof type === 'object' ? type.en || type.name || String(id) : String(id);
  return localizeData(n, language) || fallback;
}

export const formatNumber = (n, options = {}) => new Intl.NumberFormat(getLocale(), options).format(n);
export const formatDate = (value, options = {}) =>
  new Intl.DateTimeFormat(
    getLocale(),
    Object.keys(options).length ? options : { dateStyle: 'medium', timeStyle: 'short' },
  ).format(new Date(value));
export function matchesName(type, query) {
  const q = query.toLocaleLowerCase();
  const names = type.names || game.names[type.id] || {};
  return [type.name, type.en, String(type.id), ...Object.values(names), toTraditional(type.name || '')].some(
    (s) => s != null && String(s).toLocaleLowerCase().includes(q),
  );
}
const texts = new WeakMap(),
  attrs = new WeakMap(),
  numericTexts = new WeakMap();
// User-authored fields are never translated. The adapter only changes presentation,
// leaving canonical labels, model values, data attributes and event handlers intact.
function protectedNode(node) {
  const el = node.parentElement;
  if (!el) return true;
  if (el.closest('script,style,textarea,input,pre,code,[translate=no],[data-no-i18n]')) return true;
  if (
    el.closest(
      '.library-fit-notes:not(.is-empty),.library-tags,.filter-tag,.library-selected-tags .filter-tag,.fit-tag,.pilot-folder-name',
    )
  )
    return true;
  if (el.matches('.character-folder-title,.pilot-folder-title')) return true;
  if (
    el.matches('.character-person>span,.pilot-tree-person>span') &&
    !el.closest('[data-character=all5],[data-character=none],[data-person=all5],[data-person=none]')
  )
    return true;
  if (el.matches('[data-folder]>summary')) return true;
  if (
    el.closest(
      '.fit-name-row h1,.image-import-summary h3,#fit-notes-text:not(.is-empty),.library-fit-name,.plan-name,.plan-library-current,.plan-entry>span,[data-user-content]',
    )
  )
    return true;
  if (
    el.matches('.pilot-name') &&
    !el.hasAttribute('data-builtin-label') &&
    !['全技能 V', '无技能 · 基础对照'].includes(texts.get(node)?.source || node.nodeValue)
  )
    return true;
  return false;
}
function translateNode(node) {
  if (protectedNode(node)) return;
  const localizedData=node.parentElement?.dataset.localeData;if(localizedData){const value=localizeData(JSON.parse(localizedData));node.nodeValue=node.parentElement.hasAttribute('data-locale-html-text')?new DOMParser().parseFromString(value.replace(/<br\s*\/?>/gi,'\n'),'text/html').body.textContent.trim():value;return;}
  const date = node.parentElement?.dataset.localeDate;
  if (date) {
    const { value, options } = JSON.parse(date);
    node.nodeValue = formatDate(value, options);
    return;
  }
  const metric = node.parentElement?.dataset.localeMetric;
  if (metric) {
    const { value, unit, options } = JSON.parse(metric);
    node.nodeValue = formatNumber(value, options) + (unit ? ' ' + unit : '');
    return;
  }
  const message = node.parentElement?.dataset.i18n;
  if (message) {
    const params = JSON.parse(node.parentElement.dataset.i18nParams || '{}');
    node.nodeValue = t(message, params);
    return;
  }
  const value = node.nodeValue;
  if (!value?.trim()) return;
  let numeric = numericTexts.get(node);
  if (numeric && numeric.output !== value) numeric = null;
  if (!numeric && metricPresentations.has(value)) numeric = { ...metricPresentations.get(value) };
  if (numeric) {
    const output = formatNumber(numeric.value, numeric.options) + (numeric.unit ? ' ' + numeric.unit : '');
    if (value !== output) node.nodeValue = output;
    numeric.output = output;
    numericTexts.set(node, numeric);
    return;
  }
  const entity = node.parentElement?.dataset.gameEntity;
  if (entity) {
    const [table, id] = entity.split(':');
    node.nodeValue = localizeData(game.entities?.[table]?.[id]) || node.nodeValue;
    return;
  }
  const gameId = node.parentElement?.dataset.gameType;
  if (gameId) {
    node.nodeValue = gameName(Number(gameId));
    return;
  }
  let entry = texts.get(node);
  if (!entry || value !== entry.output) entry = { source: value };
  const output = translateFor(locale, entry.source);
  if (output !== value) node.nodeValue = output;
  entry.output = output;
  entry.revision = revision;
  texts.set(node, entry);
}
const attrNames = ['title', 'placeholder', 'aria-label', 'alt'];
function translateAttrs(el) {
  if (el.closest('[translate=no],[data-no-i18n]') && !el.matches('#settings-language,[name=language]'))
    return;
  if (
    !el.firstChild &&
    (el.dataset?.i18n || el.dataset?.localeMetric || el.dataset?.localeDate || el.dataset?.gameType)
  )
    el.append(document.createTextNode('—'));
  let entries = attrs.get(el) || {};
  for (const key of attrNames) {
    if(key==='aria-label'&&el.dataset?.gameTypeLabel){el.setAttribute(key,gameName(Number(el.dataset.gameTypeLabel)));continue;}
    if(key==='title'&&el.dataset?.descriptionType){const raw=localizeData(descriptions.get(Number(el.dataset.descriptionType)));if(raw){el.setAttribute(key,new DOMParser().parseFromString(raw,'text/html').body.textContent);continue;}}
    const binding = el.getAttribute('data-i18n-' + key);
    if (binding) {
      const { message, params } = JSON.parse(binding);
      el.setAttribute(key, t(message, resolveParameters(params)));
      continue;
    }
    const value = el.getAttribute(key);
    if (value == null) continue;
    if (key === 'aria-label' && el.matches('.item[data-id]')) {
      el.setAttribute(key, gameName(Number(el.dataset.id)));
      continue;
    }
    if (key === 'title' && el.hasAttribute('data-user-title')) continue;
    if (key === 'title' && el.matches('.library-fit-notes:not(.is-empty)')) continue;
    if (
      key === 'aria-label' &&
      el.matches('.pilot-tree-person') &&
      !['all5', 'none'].includes(el.dataset.person)
    )
      continue;
    let e = entries[key];
    if (!e || value !== e.output) e = { source: value };
    const output = translateFor(locale, e.source);
    if (output !== value) el.setAttribute(key, output);
    e.output = output;
    entries[key] = e;
  }
  attrs.set(el, entries);
}
export function translateTree(root = document.body) {
  if (root.nodeType === 3) {
    translateNode(root);
    return;
  }
  if (root.nodeType !== 1) return;
  translateAttrs(root);
  const walker = document.createTreeWalker(root, NodeFilter.SHOW_ELEMENT | NodeFilter.SHOW_TEXT);
  for (let n = walker.nextNode(); n; n = walker.nextNode()) {
    if (n.nodeType === 3) translateNode(n);
    else translateAttrs(n);
  }
}
let observer;
export async function setLocale(value) {
  if (!languages[value]) return;
  locale = value;
  try {
    localStorage.setItem('fitlab-language', value);
  } catch {}
  document.documentElement.lang = value;
  rebuild();
  observer?.disconnect();
  translateTree(document.body);
  document.title = t(
    'EVE FitLab · ' +
      (location.hash === '#characters' ? '角色管理' : location.hash === '#fitting' ? '装配工作台' : '装配库'),
  );
  observe();
  window.dispatchEvent(new CustomEvent('fitlab-language-change', { detail: value }));
}
function observe() {
  observer?.observe(document.body, {
    subtree: true,
    childList: true,
    characterData: true,
    attributes: true,
    attributeFilter: attrNames,
  });
}
async function readResource(url, fallback) {
  try {
    const response = await fetch(url);
    if (!response.ok) throw Error(String(response.status));
    const value = await response.json();
    if (!value || Array.isArray(value) || typeof value !== 'object') throw Error('Invalid language resource');
    return value;
  } catch (error) {
    missing.add(url);
    console.warn('FitLab language resource unavailable', url, error);
    return fallback;
  }
}
export async function initI18n() {
  const codes = Object.keys(languages),
    resources = await Promise.all(
      codes.map((code) => readResource('/locales/' + (code === 'zh-CN' ? 'source' : code) + '.json', {})),
    );
  aliases = await readResource('/locales/legacy-aliases.json', {});
  tables = Object.fromEntries(codes.map((code, index) => [code, resources[index]]));
  game = await readResource('/data/locale-game.json', { terms: {}, names: {} });
  bundles.clear();
  rebuild();
  document.documentElement.lang = locale;
  translateTree(document.body);
  document.title = t(document.title);
  observer = new MutationObserver((records) => {
    observer.disconnect();
    const roots = new Set();
    for (const r of records) {
      if (r.type === 'characterData') translateNode(r.target);
      else if (r.type === 'attributes') translateAttrs(r.target);
      else for (const n of r.addedNodes) roots.add(n);
    }
    roots.forEach((n) => translateTree(n));
    observe();
  });
  observe();
}

export function messageAttributes(key, params = {}) {
  const escape = (value) =>
    String(value).replace(
      /[&<>"']/g,
      (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c],
    );
  return 'data-i18n="' + escape(key) + '" data-i18n-params="' + escape(JSON.stringify(params)) + '"';
}
export function registerGameTypes(types) {
  for (const type of types){if(type.names&&Object.keys(type.names).length)game.names[type.id]=type.names;if(type.descriptionNames)descriptions.set(type.id,type.descriptionNames);}
}
export function gameNameMarkup(type) {
  if(typeof type==='object')registerGameTypes([type]);
  const id = typeof type === 'object' ? type.id : type;
  return (
    '<span data-game-type="' +
    Number(id) +
    '">' +
    gameName(type).replace(
      /[&<>"']/g,
      (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c],
    ) +
    '</span>'
  );
}

export function localizeData(value, language = locale) {
  if (typeof value === 'string') return value;
  if (!value) return '';
  return language === 'zh-TW'
    ? toTraditional(value.zh || value.en || '')
    : value[language === 'zh-CN' ? 'zh' : language] || value.en || value.zh || '';
}

export function gameLabelMarkup(table, id, fallback = '') {
  const value = localizeData(game.entities?.[table]?.[id]) || fallback;
  return (
    '<span data-game-entity="' +
    table +
    ':' +
    Number(id) +
    '">' +
    String(value).replace(
      /[&<>"']/g,
      (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c],
    ) +
    '</span>'
  );
}

// Declarative numeric bindings preserve the original precision and units on a live switch.
export function formatMetric(value, unit = '', options = { maximumFractionDigits: 2 }) {
  if (!Number.isFinite(value)) return '—';
  const result = formatNumber(value, options) + (unit ? ' ' + unit : '');
  metricPresentations.set(result, { value, unit, options });
  if (metricPresentations.size > 4096) metricPresentations.delete(metricPresentations.keys().next().value);
  return result;
}
export function metricAttributes(text) {
  const spec = metricPresentations.get(text);
  return spec
    ? 'data-locale-metric="' +
        JSON.stringify(spec).replace(/&/g, '&amp;').replace(/"/g, '&quot;').replace(/</g, '&lt;') +
        '"'
    : '';
}

export function navigationMarkup(path, label) {
  const id = game.navigationPaths?.[path];
  return id
    ? gameLabelMarkup('marketGroups', id, label)
    : String(translateFor(locale, label)).replace(
        /[&<>"']/g,
        (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c],
      );
}

function resolveParameters(params) {
  return Object.fromEntries(
    Object.entries(params).map(([key, value]) => [
      key,
      value && typeof value === 'object'
        ? value.message
          ? t(value.message)
          : value.typeId
            ? gameName(value.typeId)
            : value
        : value,
    ]),
  );
}
export function setTranslatedAttribute(el, attribute, message, params) {
  el.setAttribute('data-i18n-' + attribute, JSON.stringify({ message, params }));
  el.setAttribute(attribute, t(message, resolveParameters(params)));
}

export function attributeMessageMarkup(attribute, message, params) {
  const escape = (value) =>
    String(value).replace(
      /[&<>"']/g,
      (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c],
    );
  return (
    'data-i18n-' +
    attribute +
    '="' +
    escape(JSON.stringify({ message, params })) +
    '" ' +
    attribute +
    '="' +
    escape(t(message, resolveParameters(params))) +
    '"'
  );
}

export function withPresentationLocale(language, render) {
  const previous = presentationLocale,
    metrics = new Map(metricPresentations);
  presentationLocale = languages[language] ? language : locale;
  try {
    return render();
  } finally {
    presentationLocale = previous;
    metricPresentations.clear();
    for (const [key, value] of metrics) metricPresentations.set(key, value);
  }
}

export function dateMarkup(
  value,
  options = {
    year: 'numeric',
    month: 'numeric',
    day: 'numeric',
    hour: 'numeric',
    minute: '2-digit',
    second: '2-digit',
  },
) {
  const escape = (text) =>
    String(text).replace(
      /[&<>"']/g,
      (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c],
    );
  return (
    '<span data-locale-date="' +
    escape(JSON.stringify({ value, options })) +
    '">' +
    escape(formatDate(value, options)) +
    '</span>'
  );
}

export function setMessage(el,message,params={}){el.dataset.i18n=message;el.dataset.i18nParams=JSON.stringify(params);el.textContent=t(message,params);}

export function metricMarkup(value,unit='',options={maximumFractionDigits:2}){const text=formatMetric(value,unit,options);return '<span '+metricAttributes(text)+'>'+text.replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]))+'</span>';}
