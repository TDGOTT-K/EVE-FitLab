# RC.2 首启装配库阻塞修复

UI冻结提交a6f54df；引擎仍dfcbdb0 / 0.210.4-ui.perf1 / r57。rc.1未覆盖。

根因：fighter-ui.js顶层await /api/fighters，阻塞整个app.js模块。原目录通过catalog_search冷启动引擎并建立完整搜索目录，再扫描typeDogma；实测9255ms。空装配库也必须等它结束。

修复：目录使用服务启动时已加载的同一固定SDE metadata，保留发布状态、类别、分类、最大数量、名称和meta值。53行与旧公开查询投影逐项一致；来源改为已有metadata来源（含build、index/archive SHA、来源URL），不表示支持所有游戏机制。前端独立加载，结束后刷新依赖视图，不再将目录作为应用模块加载前提。

同机独立桌面测试目录，rc.1与rc.2：启动到库可用12860→4171ms；waitForURL(load)后等待树可见8352→29ms；舰载机HTTP请求9255→28ms。单次样本，非跨机性能保证；外壳前后台启动仍约数秒。没有将29ms当成完整首启时间。

验证：慢fetch不阻塞模块初始化专项；72模块检查；53行与旧实现一致；rc.2实际打包Electron中的分析、预览、安装、保存、撤销重做与原生导出哈希一致。证据output/startup-after-001/validation.json与library.png。未重跑真实外部SSO、干净VM安装卸载。只做本地候选，不发布。
