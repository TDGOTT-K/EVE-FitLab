(() => {
  const dictionary = window.siteTranslations;
  const supported = ['zh-CN', 'zh-TW', 'en', 'ja'];
  const records = [];
  const walker = document.createTreeWalker(document.documentElement, NodeFilter.SHOW_TEXT);
  while (walker.nextNode()) {
    const node = walker.currentNode;
    if (node.parentElement.closest('script, style, select, #toast')) continue;
    const key = node.textContent.trim();
    if (dictionary[key]) records.push({node, key, original: node.textContent});
  }
  const attributes = [];
  document.querySelectorAll('[aria-label], [alt], meta[name="description"]').forEach(node => {
    for (const name of ['aria-label', 'alt', 'content']) {
      const key = node.getAttribute(name);
      if (dictionary[key]) attributes.push({node, name, key});
    }
  });
  function detect() {
    for (const language of navigator.languages || [navigator.language]) {
      if (/^zh-(TW|HK|MO|Hant)/i.test(language)) return 'zh-TW';
      if (/^zh/i.test(language)) return 'zh-CN';
      if (/^ja/i.test(language)) return 'ja';
      if (/^en/i.test(language)) return 'en';
    }
    return 'en';
  }
  let saved;
  try { saved = localStorage.getItem('fitlab-site-language'); } catch {}
  let locale = supported.includes(saved) ? saved : 'zh-CN';
  window.siteT = key => dictionary[key]?.[locale] || key;
  function apply(value) {
    locale = supported.includes(value) ? value : 'en';
    document.documentElement.lang = locale;
    for (const {node,key,original} of records) node.textContent = original.replace(key, window.siteT(key));
    for (const {node,name,key} of attributes) node.setAttribute(name, window.siteT(key));
    document.querySelector('#site-language').value = locale;
    try { localStorage.setItem('fitlab-site-language', locale); } catch {}
  }
  document.querySelector('#site-language').addEventListener('change', event => apply(event.target.value));
  apply(locale);
})();
