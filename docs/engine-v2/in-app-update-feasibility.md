# 应用内更新可行性调查

2026-09-21。范围：调查与方案，未接入更新器、未修改发布渠道。

## 结论

可采用 electron-updater + 现有 Windows NSIS 包。用户体验为检查更新、下载进度、重启并更新；后台仍由安装器替换程序文件，不需要用户自行下载、选择目录和重新安装。已经发布的 rc.6 没有更新模块，首次启用仍需手动安装一次带更新器的版本。

## 当前证据

- package.json 使用 Electron 44.3.0、electron-builder 26.15.3、NSIS、按用户安装和固定 appId；未安装 electron-updater，也没有 publish 配置。
- desktop/main.cjs 没有更新检查或 IPC；preload 目前只暴露目录选择。退出时会强制结束后端进程树，更新前必须先完成保存与写事务。
- rc.6 本地已有 latest.yml、安装器和 .blockmap；实际 GitHub Release 只有安装器、来源/校验清单、引擎源码包，没有发布更新描述或 blockmap。
- 数据与安装目录分离：默认 `%LOCALAPPDATA%/EVE-FitLab/Data`，支持自选目录；Agent 为独立状态。安装配置不删除应用数据。这提供了保留数据的基础，但不能代替升级测试。
- nengine_adapter.validate_source_binding 严格比较引擎、规则、接口和 SDE 来源；更换程序后，旧原生会话不一定可直接使用。更新器不能通过重写来源标签绕过校验。

## 建议实现

1. Electron 主进程集成 electron-updater，以正式依赖打进包；只向应用自己的渲染窗口暴露有限的检查、下载、状态与安装 IPC。
2. 设置中增加当前版本/检查更新；发现新版提示说明和大小，用户选择下载。下载完成后提供“保存并重启更新”和“稍后”。不在编辑中强制重启，不默认设置退出即安装。
3. 首先沿用 GitHub Releases；自动发布安装器、对应通道 YAML 和 blockmap，并验证其引用文件。当前 rc 预发布必须显式配置/测试预览通道，稳定版不会误收到预览版。不在客户端内置 GitHub 凭据。
4. 下载可使用差分机制，失败回退完整包；不承诺每次小补丁，也不承诺无需重启。失败保留旧版本和用户配置。
5. 安装前完成当前装配保存、确认没有未决写事务，备份实际数据目录与必要的配置，关闭 Python/.NET 后端。若外部 MCP 客户端正在使用包内 Agent，应提示退出相关进程后再替换，不能直接强杀用户正在运行的任务。
6. 程序、网页、Python、Agent、.NET、SDE 与来源清单作为同一发布单元更新，避免混搭。旧版本原生会话保留；需要重新建立当前版本会话时采用明确迁移/重新计算流程，不能伪造旧来源。第一版可对不兼容更新明确阻止自动安装并提示处理方式。
7. 规划 Windows 代码签名和发布来源验证。现有包未签名；文件哈希验证只证明下载内容匹配更新描述，不能独立证明发布者身份。选定并锁定 updater 版本，核对其签名政策，不靠关闭验证解决接入。

## 难度与验收

基础更新入口难度较低，可靠交付为中等；主要工作在数据/写事务、进程退出和发布流水线。首版不建议自行开发热更新或文件补丁系统。

至少用两份实际安装包验证：旧→新更新、预览/稳定通道隔离、同版本不更新、断网与下载失败可重试、校验失败拒绝安装、未保存修改和未决写事务保护、自选数据目录保留、旧原生会话兼容提示、Agent占用文件、安装后版本与引擎/SDE清单一致。保存旧包供人工回退；数据已迁移时不能承诺无条件自动降级。

## 参考

- electron-builder 官方文档源码：[Auto Update](https://github.com/electron-userland/electron-builder/blob/master/website/docs/features/auto-update.md)
- [NSIS updater 实现](https://github.com/electron-userland/electron-builder/blob/master/packages/electron-updater/src/NsisUpdater.ts)
- [GitHub provider / 预发布通道选择](https://github.com/electron-userland/electron-builder/blob/master/packages/electron-updater/src/providers/GitHubProvider.ts)

以上在线源码为调查时的 master，不代表应无条件升级到其开发版。落地时应选定兼容稳定版本并重新验收。
