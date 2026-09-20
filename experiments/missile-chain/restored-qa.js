async(page)=>{
 await page.waitForFunction(()=>!!window.chainDemo);
 const states=[];
 for(const t of [17.5,18.8,19.2,21,25.8,27]){
   states.push(await page.evaluate(t=>window.chainDemo.seek(t),t));
   await page.waitForTimeout(100);
   await page.screenshot({path:`output/playwright/ship-assets/restored-${t}.png`});
 }
 const a=await page.evaluate(()=>window.chainDemo.seek(18.8));
 if(JSON.stringify(a)!==JSON.stringify(states[1]))throw Error('Animation seek mismatch');
 await page.waitForTimeout(350);
 const b=await page.evaluate(()=>window.chainDemo.getState());
 if(b.time!==18.8||JSON.stringify(a.loaderPosition)!==JSON.stringify(b.loaderPosition))throw Error('Pause drift');
 if(new Set(states.map(s=>JSON.stringify(s.loaderPosition))).size<2)throw Error('Bones static');
 const fire=await page.evaluate(()=>window.chainDemo.seek(19));
 const muzzleGap=Math.hypot(...fire.position.map((v,i)=>v-fire.muzzle[i]));
 if(muzzleGap>1e-6)throw Error('Muzzle mismatch');
 return {passed:true,states,muzzleGap,clips:await page.evaluate(()=>window.chainDemo.clips)};
}
