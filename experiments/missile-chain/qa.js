async(page)=>{
 await page.waitForFunction(()=>!!window.chainDemo);
 await page.setViewportSize({width:1440,height:960});
 const a=await page.evaluate(()=>window.chainDemo.seek(21));
 await page.waitForTimeout(300);
 await page.screenshot({path:'output/playwright/ship-assets/chain-flight.png'});
 await page.getByRole('button',{name:'发射器特写',exact:true}).click();
 await page.evaluate(()=>window.chainDemo.seek(19.08));
 await page.waitForTimeout(200);
 await page.screenshot({path:'output/playwright/ship-assets/chain-launcher.png'});
 await page.getByRole('button',{name:'目标特写',exact:true}).click();
 const impact=await page.evaluate(()=>window.chainDemo.seek(24.55));
 await page.waitForTimeout(200);
 await page.screenshot({path:'output/playwright/ship-assets/chain-impact.png'});
 const b=await page.evaluate(()=>window.chainDemo.seek(21));
 if(JSON.stringify(a)!==JSON.stringify(b))throw Error('Seek is not deterministic');
 await page.waitForTimeout(400);
 const paused=await page.evaluate(()=>window.chainDemo.getState());
 if(paused.time!==21||paused.playing)throw Error('Pause drift');
 await page.getByRole('combobox',{name:'播放速度'}).selectOption('2');
 await page.getByRole('button',{name:'播放',exact:true}).click();
 await page.waitForTimeout(500);
 const advanced=await page.evaluate(()=>window.chainDemo.getState());
 if(advanced.time<=21.5||advanced.speed!==2)throw Error('Speed control failed');
 await page.getByRole('button',{name:'全景',exact:true}).click();
 await page.getByRole('combobox',{name:'播放速度'}).selectOption('1');
 await page.getByRole('button',{name:'重播',exact:true}).click();
 return {deterministic:true,pauseStable:true,flight:a,impact,advanced};
}
