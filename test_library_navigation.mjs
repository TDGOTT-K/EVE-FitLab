import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';
import {sameSavedContent} from './fit-save-controller.js';
const source=fs.readFileSync(new URL('./app.js',import.meta.url),'utf8');
const route=source.slice(source.indexOf('async function navigateFitPage(){'),source.indexOf("$('#library-search').oninput=drawFitLibrary;"));
function fixture(){
 const elements=new Map(),element=id=>{if(!elements.has(id))elements.set(id,{hidden:false,textContent:'',setAttribute(){},removeAttribute(){}});return elements.get(id)};
 let resolveSave,rejectSave,reads=0,draws=0,draft;
 const save=new Promise((resolve,reject)=>{resolveSave=resolve;rejectSave=reject});
 const fit={id:'A',name:'changed A',shipId:1,slots:[]};
 const context=vm.createContext({navigationVersion:0,pageMode:'editor',lastWorkPage:'editor',editorFitDeleted:false,libraryLoaded:true,libraryFits:[{...fit,name:'saved A'}],libraryNavigationSaves:new Map(),fitRecord:fit,
  location:{hash:'#library'},document:{title:''},$:element,characterManager:{show(){},hide(){}},flow:{open:false},capturePageScroll(){},restorePageScroll(){},closeMenu(){},t:s=>s,say:s=>{context.message=s},
  currentFit:()=>structuredClone(fit),storeWorkingDraft:()=>{draft=structuredClone(fit)},localStorage:{getItem:()=>JSON.stringify(draft)},byId:()=>true,sameSavedContent,
  persistFit:()=>save,api:async()=>{reads++;return [{...fit,revision:2}]},libraryTree:{update(){}},drawFitLibrary:()=>{draws++}});
 vm.runInContext(route,context);
 return {context,element,resolveSave,rejectSave,get reads(){return reads},get draws(){return draws}};
}
const a=fixture(),pending=a.context.navigateFitPage();
assert.equal(a.element('#editor-page').hidden,true);
assert.equal(a.element('#library-page').hidden,false);
assert.equal(a.draws,1);assert.equal(a.reads,0);
assert.equal(a.context.libraryFits[0].name,'changed A');assert.equal(a.context.libraryFits[0]._workingDraft,true);
assert(a.context.libraryNavigationSaves.has('A'));
a.resolveSave({id:'A',name:'changed A',shipId:1,slots:[],revision:2});await pending;
assert(!a.context.libraryNavigationSaves.has('A'));assert.equal(a.reads,1);
const b=fixture(),failed=b.context.navigateFitPage();b.rejectSave(Error('offline'));await failed;
assert(b.context.message.includes('保存失败'));assert.equal(b.element('#library-page').hidden,false);assert(!b.context.libraryNavigationSaves.has('A'));
const c=fixture(),stale=c.context.navigateFitPage();c.context.navigationVersion++;c.context.pageMode='editor';c.context.fitRecord={id:'B'};c.resolveSave({id:'A',name:'changed A',shipId:1,slots:[],revision:3});await stale;
assert.equal(c.reads,0);assert.equal(c.context.fitRecord.id,'B');assert.equal(c.context.libraryFits[0].revision,3);
console.log('Library navigation switches before pending save; failures retain draft, stale receipts never navigate or overwrite another editor.');
