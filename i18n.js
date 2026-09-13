import {localeOverrides} from './locale-overrides.js';
import {toTraditional} from './locale-vendor.js';
export const languages={'zh-CN':'简体中文','zh-TW':'繁體中文',en:'English',ja:'日本語'};
const system=()=>{const n=navigator.language||'zh-CN';return /^(zh-(TW|HK|MO|Hant))/i.test(n)?'zh-TW':n.startsWith('zh')?'zh-CN':n.startsWith('ja')?'ja':'en'};
let locale;try{locale=localStorage.getItem('fitlab-language')}catch{}locale=languages[locale]?locale:system();
let tables={},game={terms:{},names:{}},phrases=[],pattern=null,revision=0;
export const getLocale=()=>locale;
const quote=s=>s.replace(/[.*+?^${}()|[\]\\]/g,'\\$&');
const bundles=new Map();
function bundle(language){if(bundles.has(language))return bundles.get(language);const map={};for(const [source,translations] of Object.entries(game.terms))map[source]=language==='zh-TW'?toTraditional(source):translations[language]||translations.en||source;Object.assign(map,language==='zh-CN'?{}:tables.en||{},tables[language]||{},localeOverrides[language]||{});const keys=Object.keys(map).filter(k=>k&&/[\u3400-\u9fff]/.test(k)).sort((a,b)=>b.length-a.length);const b={map,pattern:keys.length?new RegExp(keys.map(quote).join('|'),'g'):null};bundles.set(language,b);return b;}
function rebuild(){const b=bundle(locale);phrases=b.map;pattern=b.pattern;revision++;}
export function translateFor(language,source){if(source==null)return '';const text=String(source);if(language==='zh-CN')return text;const b=bundle(language);let result=b.map[text]??(b.pattern?text.split(/(「[^」]*」)/g).map(part=>part.startsWith('「')?part:part.replace(b.pattern,s=>b.map[s])).join(''):text);return language==='zh-TW'?toTraditional(result):result;}
export function t(source,params={}){return translateFor(locale,source).replace(/\{(\w+)\}/g,(m,k)=>params[k]??m)}
export function gameName(type){const id=typeof type==='object'?type.id:type,n=game.names[id];if(!n)return typeof type==='object'?type.name||type.en||String(id):String(id);return locale==='zh-CN'?n.zh||n.en:locale==='zh-TW'?toTraditional(n.zh||n.en):n[locale]||n.en;}
export const formatNumber=(n,options={})=>new Intl.NumberFormat(locale,options).format(n);
export const formatDate=(value,options={})=>new Intl.DateTimeFormat(locale,{dateStyle:'medium',timeStyle:'short',...options}).format(new Date(value));
export function matchesName(type,query){const q=query.toLocaleLowerCase();const names=game.names[type.id]||{};return [type.name,type.en,String(type.id),...Object.values(names),toTraditional(type.name||'')].some(s=>String(s).toLocaleLowerCase().includes(q))}
const texts=new WeakMap(),attrs=new WeakMap();
// User-authored fields are never translated. The adapter only changes presentation,
// leaving canonical labels, model values, data attributes and event handlers intact.
function protectedNode(node){const el=node.parentElement;if(!el)return true;if(el.closest('script,style,textarea,input,[translate=no],[data-no-i18n]'))return true;
 if(el.closest('.library-fit-notes,.library-tags,.filter-tag,.library-selected-tags .filter-tag,.fit-tag,.pilot-folder-name'))return true;
 if(el.matches('.character-folder-title,.pilot-folder-title'))return true;if(el.matches('.character-person>span,.pilot-tree-person>span')&&!el.closest('[data-character=all5],[data-character=none],[data-person=all5],[data-person=none]'))return true;if(el.matches('[data-folder]>summary'))return true;
 if(el.closest('.fit-name-row h1,.image-import-summary h3'))return true;if(el.matches('.pilot-name')&&!['全技能 V','无技能 · 基础对照'].includes(node.nodeValue))return true;
 return false;}
function translateNode(node){if(protectedNode(node))return;const value=node.nodeValue;if(!value?.trim())return;let entry=texts.get(node);if(!entry||value!==entry.output)entry={source:value};const output=t(entry.source);if(output!==value)node.nodeValue=output;entry.output=output;entry.revision=revision;texts.set(node,entry)}
const attrNames=['title','placeholder','aria-label','alt'];
function translateAttrs(el){if(el.closest('[translate=no],[data-no-i18n]'))return;let entries=attrs.get(el)||{};for(const key of attrNames){const value=el.getAttribute(key);if(value==null)continue;if(key==='title'&&el.matches('.library-fit-notes'))continue;if(key==='aria-label'&&el.matches('.pilot-tree-person')&&!['all5','none'].includes(el.dataset.person))continue;let e=entries[key];if(!e||value!==e.output)e={source:value};const output=t(e.source);if(output!==value)el.setAttribute(key,output);e.output=output;entries[key]=e;}attrs.set(el,entries)}
export function translateTree(root=document.body){if(root.nodeType===3){translateNode(root);return;}if(root.nodeType!==1)return;translateAttrs(root);const walker=document.createTreeWalker(root,NodeFilter.SHOW_ELEMENT|NodeFilter.SHOW_TEXT);for(let n=walker.nextNode();n;n=walker.nextNode()){if(n.nodeType===3)translateNode(n);else translateAttrs(n)}}
let observer;
export async function setLocale(value){if(!languages[value])return;locale=value;try{localStorage.setItem('fitlab-language',value)}catch{}document.documentElement.lang=value;rebuild();observer?.disconnect();translateTree(document.body);document.title=t('EVE FitLab · '+(location.hash==='#characters'?'角色管理':location.hash==='#fitting'?'装配工作台':'装配库'));observe();window.dispatchEvent(new CustomEvent('fitlab-language-change',{detail:value}));}
function observe(){observer?.observe(document.body,{subtree:true,childList:true,characterData:true,attributes:true,attributeFilter:attrNames})}
export async function initI18n(){const [source,en,tw,ja,data]=await Promise.all(['source','en','zh-TW','ja'].map(code=>fetch('/locales/'+code+'.json').then(r=>{if(!r.ok)return {};return r.json()})).concat(fetch('/data/locale-game.json').then(r=>r.json())));tables={'zh-CN':source,en,'zh-TW':tw,ja};game=data;bundles.clear();rebuild();document.documentElement.lang=locale;translateTree(document.body);document.title=t(document.title);observer=new MutationObserver(records=>{observer.disconnect();const roots=new Set();for(const r of records){if(r.type==='characterData')translateNode(r.target);else if(r.type==='attributes')translateAttrs(r.target);else for(const n of r.addedNodes)roots.add(n);}roots.forEach(n=>translateTree(n));observe()});observe();}
