import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';
const source=fs.readFileSync(new URL('./app.js',import.meta.url),'utf8');
const update=source.slice(source.indexOf('function updateShip('),source.indexOf('function restoreFit('));
const cruiser={id:1,name:'T3',en:'T3',attrs:{14:0,13:0,12:0,1137:3}};
const frigate={id:2,name:'Frigate',en:'Frigate',attrs:{14:3,13:3,12:3,1137:3}};
const context={ship:cruiser,fitRecord:{shipId:1,name:'Fit'},counts:{high:6,mid:6,low:4,rig:3,subsystem:4},
 byId:id=>id===1?cruiser:frigate,subsystemSlots:s=>s.id===1?[1,2,3,4]:[],
 $:()=>({style:{}}),renderFitTags(){},renderShipBadge(){},renderPilot(){},gameNameMarkup:()=>'',esc:s=>s};
vm.createContext(context);vm.runInContext(update,context);
for(let i=0;i<4;i++){context.updateShip();assert.equal(context.counts.high,6);assert.equal(context.counts.mid,6);assert.equal(context.counts.low,4);}
context.fitRecord.shipId=2;context.updateShip();assert.equal(context.counts.high,3);assert.equal(context.counts.subsystem,0);
context.fitRecord.shipId=1;context.updateShip(true);assert.equal(context.counts.high,0);assert.equal(context.counts.subsystem,4);
console.log('T3 calculated slot counts survive same-hull refresh; hull changes and explicit fit restore reset counts.');
