# 文档索引

当前事实以根 README、独立引擎基线清单和代码入口为准。旧日期、r33/r43/r54 文档是当时的设计/验收记录，不是当前运行配置。

| 路径 | 职责 |
| --- | --- |
| `../app.js`、根目录 JS/CSS/HTML | 当前浏览器工作台与共享展示组件 |
| `../server.py`、`../nengine_*.py` | 本地 HTTP API、存储、公开 MCP 适配 |
| `../desktop/` | Electron 与 Windows 打包 |
| `../scripts/` | 当前数据生成、检查和启动工具 |
| `../locales/`、`../data/`、`../assets/` | 语言、必要展示数据、资源与第三方声明 |
| `../website/` | 官网唯一源码与历史下载信息 |
| `../experiments/` | 研究输入与原型，不代表产品功能完成 |
| `engine-v2/` | NEngine 接入设计、逐批验收与未完成事项 |
| `images/` | README 等文档使用的插图 |

## 当前必读

- [本次整理及验证](workspace-cleanup.md)
- [阶段一产品定义](phase1-definition-and-gates.md)
- [阶段一发布边界](engine-v2/phase1-release.md)
- [最新工作台性能与契约记录](engine-v2/workbench-performance-r59.md)
- [本地化](localization.md) 与 [翻译工作记录](localization-work-log.md)
- [联合开发历史台账](engine-v2/joint-development.md)

其他早期设计和验收材料保留用于追溯，不应直接照搬其中的本机绝对路径、旧端口或旧版本宣称。冻结契约、来源哈希、引擎补丁台账和历史缺口记录不因整理而重写。
