export function shipInfoMarkup(data,catalog,{esc,local,number}){
 const plain=text=>new DOMParser().parseFromString(local(text).replace(/<br\s*\/?>/gi,'\n'),'text/html').body.textContent||'';
 const groups=new Map();
 for(const trait of data.shipTraits?.items||[]){
  const skill=catalog.find(t=>t.id===trait.skillTypeId);
  const title=trait.kind==='skill_per_level'?(skill?.name||'技能 #'+trait.skillTypeId)+' · 每级收益':trait.kind==='role'?'特有加成':'其他特性';
  if(!groups.has(title))groups.set(title,[]);
  const unit=data.shipTraits?.units?.[trait.unitId];
  const suffix=trait.unitId===105?'%':local(unit?.displayName)||'';
  const value=Number.isFinite(trait.bonus)?number(trait.bonus)+(suffix?' '+suffix:''):'';
  groups.get(title).push('<li>'+(value?'<b>'+esc(value)+'</b> ':'')+esc(plain(trait.text))+'</li>');
 }
 const traits=[...groups].map(([title,items])=>'<section class="ship-info-group"><h3>'+esc(title)+'</h3><ul>'+items.join('')+'</ul></section>').join('')||'<p class="profile-note">当前来源未提供特性说明。</p>';
 const skills=data.requiredSkills;
 const required=Array.isArray(skills)?skills.map(s=>'<div class="ship-required-skill"><span>'+esc(local(s.names)||catalog.find(t=>t.id===s.skillTypeId)?.name||'#'+s.skillTypeId)+'</span><b>'+esc(Number.isInteger(s.level)?['0','I','II','III','IV','V'][s.level]??s.level:'等级未知')+'</b></div>').join('')||'<p class="profile-note">未声明直接技能前置。</p>':'<p class="profile-note">技能要求暂不可用。</p>';
 return '<div data-info-pane="traits">'+traits+'<p class="profile-note">特性说明 · 加成是否已计算以装配结果为准</p></div><div data-info-pane="requirements"><h3>所需技能</h3>'+required+'<p class="profile-note">物品直接前置要求；不代表当前角色已满足全部技能链。</p></div>';
}
