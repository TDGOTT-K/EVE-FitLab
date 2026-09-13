// Consistent 24-unit line icons for share-image information hierarchy.
export const shareIconPaths={
 attack:'M4 20 20 4M13 4h7v7M4 13v7h7M5 5l4 4m6 6 4 4',
 shield:'M12 2 3 6v6c0 5 9 10 9 10s9-5 9-10V6Z M8 12l3 3 5-6',
 armor:'M6 3h12l3 7-3 11H6L3 10Z M6 10h12M12 3v18',
 structure:'M12 2 3 7v10l9 5 9-5V7Z M3 7l9 5 9-5M12 12v10',
 capacitor:'M9 2h6M5 5h14v17H5Z M13 8l-4 6h4l-2 5',
 speed:'M3 17a9 9 0 1 1 18 0M12 14l5-7M5 20h14',
 cpu:'M6 6h12v12H6Z M9 9h6v6H9Z M8 2v4m4-4v4m4-4v4M8 18v4m4-4v4m4-4v4M2 8h4m-4 4h4m-4 4h4m12-8h4m-4 4h4m-4 4h4',
 power:'M14 2 4 14h7l-1 8 10-13h-7Z',
 range:'M12 2v4m0 12v4M2 12h4m12 0h4M4 12a8 8 0 1 0 16 0 8 8 0 1 0-16 0M9 12h6',
 cycle:'M12 3a9 9 0 1 1-8 5M3 3v5h5M12 7v6l4 2',
 drone:'M8 8h8v8H8Z M3 3l5 5m8 8 5 5M21 3l-5 5M8 16l-5 5M2 6V2h4m12 0h4v4M2 18v4h4m12 0h4v-4',
 cargo:'M3 7l9-5 9 5v14H3Z M3 7h18M9 7v5h6V7',
 slots:'M4 4h16v4H4Zm0 6h16v4H4Zm0 6h16v4H4Z',
 repair:'M9 3h6v6h6v6h-6v6H9v-6H3V9h6Z',
 notes:'M5 3h14v18H5Z M8 7h8m-8 5h8m-8 5h5',
 qr:'M3 3h6v6H3Zm12 0h6v6h-6ZM3 15h6v6H3Zm12 0h3v3h3v3h-6ZM3 12h9V3m0 12v6m6-9h3',
};
export function shareIconFor(label){if(/CPU/.test(label))return 'cpu';if(/栅格/.test(label))return 'power';if(/电容|耗电|传电|毁电|吸电/.test(label))return 'capacitor';if(/DPS|攻击|伤害|齐射/.test(label))return 'attack';if(/护盾|防御|EHP/.test(label))return 'shield';if(/装甲/.test(label))return 'armor';if(/结构/.test(label))return 'structure';if(/修|回盾/.test(label))return 'repair';if(/速度|机动|跃迁/.test(label))return 'speed';if(/距离|射程|失准|锁定|扫描/.test(label))return 'range';if(/周期|单轮|时间/.test(label))return 'cycle';if(/无人机|带宽/.test(label))return 'drone';if(/货舱|容量|机库/.test(label))return 'cargo';if(/备注/.test(label))return 'notes';if(/导入|装配码/.test(label))return 'qr';return 'slots';}
