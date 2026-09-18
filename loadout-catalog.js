// All source values come from the same pinned SDE as the fitting engine.
const response=await fetch('./api/loadout-catalog');
if(!response.ok)throw Error('脑插与增效剂目录读取失败');
const catalog=await response.json();
export const implantCatalog=catalog.implants;
export const boosterCatalog=catalog.boosters;
export const loadoutCatalogSource=catalog.source;
