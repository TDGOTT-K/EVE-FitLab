// Run with playwright-cli run-code --filename scripts/check-info-close.cjs
// Requires the local web server on port 5207; no calculation engine is needed.
async page => {
 await page.goto('http://127.0.0.1:5207/#fitting');
 await page.evaluate(()=>document.documentElement.classList.add('desktop-app'));
 await page.locator('#search').fill('轻型快速导弹发射器 II');
 const open=async()=>{await page.locator('.item').filter({hasText:'轻型快速导弹发射器 II'}).first().click({button:'right'});await page.getByRole('menuitem',{name:'查看信息',exact:true}).click();await page.locator('.info-tabs').waitFor();};
 for(const height of [720,910]){
  await page.setViewportSize({width:1069,height});await open();
  const geometry=await page.locator('#info-window').evaluate(panel=>{const close=document.querySelector('#info-close'),header=document.querySelector('header');return {closeY:close.getBoundingClientRect().top,headerBottom:header.getBoundingClientRect().bottom,panelRegion:getComputedStyle(panel).webkitAppRegion,buttonRegion:getComputedStyle(close).webkitAppRegion};});
  if(geometry.closeY>=geometry.headerBottom)throw Error('Regression setup must overlap native titlebar');
  if(geometry.panelRegion!=='no-drag'||geometry.buttonRegion!=='no-drag')throw Error('Native titlebar can intercept close clicks');
  await page.locator('#info-close').click();if(await page.locator('#info-window').isVisible())throw Error('Close click failed');
  await open();await page.keyboard.press('Escape');if(await page.locator('#info-window').isVisible())throw Error('Escape failed');
 }
 await open();
 const bar=await page.locator('.info-titlebar').boundingBox();
 await page.mouse.move(bar.x+4,bar.y+bar.height/2);await page.mouse.down();await page.mouse.move(bar.x+44,bar.y+bar.height/2+55,{steps:5});await page.mouse.up();
 const moved=await page.locator('.info-titlebar').boundingBox();if(moved.x<=bar.x)throw Error('JS titlebar dragging broken');
 await page.locator('#info-close').click();if(await page.locator('#info-window').isVisible())throw Error('Close after dragging failed');
}
