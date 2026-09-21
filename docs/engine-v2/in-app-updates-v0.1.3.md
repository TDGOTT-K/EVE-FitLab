# v0.1.3 应用内更新

## 用户流程

桌面启动后检查官网，发现新版时设置入口显示提示。设置 → 应用更新：检查、下载、保存并重启更新。下载不触发安装，退出应用也不会偷偷安装。已发布的 rc.6 无更新器，需手动安装一次 v0.1.3。浏览器开发版不显示安装入口。

官网只提供小型静态文件：`https://imfishman.com/updates/stable/manifest.json` 和 `latest.yml`；安装器及 blockmap 由 GitHub Releases 提供。使用稳定通道，拒绝预发布与降级。引擎版本、静态规则、接口和 SDE 来源不一致时不自动安装，明确提示手动处理，禁止伪造旧装配的来源绑定。

## 完整性与数据

签名清单使用 Ed25519，客户端嵌入公钥。版本、引擎来源、安装器地址、大小与 SHA-512 均在签名内。只接受指定 GitHub 仓库的版本化安装器，核对 electron-updater 的下载描述，下载后及安装前核验实际文件。禁用 NSIS web installer。更新进程不接受渲染器指定 URL、命令或文件路径；IPC 只接受本窗口主框架。

这不是 Windows Authenticode 签名，现有安装包仍可能出现 Windows 未知发布者提示。没有通过关闭文件验证来实现更新。私钥在发布者本机 `%LOCALAPPDATA%/EVE-FitLab-Release/update-signing-private.pem`，不入库、不打包、不上传；需单独离线备份。丢失私钥时不能重新生成同名身份覆盖已安装版本的信任。

安装前检查包内 Agent 占用，要求先断开客户端；当前编辑/保存未确认时拒绝安装。保存后在应用写锁内备份实际数据目录及 storage.json，并核验文件哈希；路径支持自选目录。备份位于应用配置目录的 UpdateBackups，拒绝递归备份和外部链接。未完成备份不会退出安装。后台进程在退出时按现有逻辑结束，安装器整体替换应用、Python、Agent、.NET 和资源。

## 发布步骤

1. 按当前 package.json 构建。必须使用真实的生产依赖树；不要把开发 node_modules 的 junction 当成打包环境，否则可能漏掉 electron-updater 的嵌套依赖。
2. 对生成的安装器、包内桌面/Agent 做验证。文件版本必须是稳定 semver。
3. 运行 `node scripts/create-update-feed.cjs INSTALLER BASELINE NOTES_JSON OUTPUT_DIRECTORY`。它读取既有私钥、生成并验证签名清单；不要在普通用户客户端运行签名脚本。
4. 先上传并公开安装器和 blockmap，验证公开下载哈希，再部署官网 updates 目录。官网 `/updates/*` 配置 no-store。不要先指向不存在的安装器。
5. 版本清单与安装器保持原子的一组内容；任何版本/哈希不一致都会阻止更新，稍后可重新检查。

## 验证

`node test_desktop_updates.cjs` 覆盖签名、下载地址、来源绑定、同版本、错误元数据、下载失败重试、未保存导致安装失败后重试、并发操作及文件篡改。`python -m unittest test_update_backup` 覆盖自选目录、配置保留、哈希与防止备份目录递归。

真实 NSIS 测试使用独立 appId / 安装目录，从 0.1.3 测试壳更新到 0.1.4 测试壳，使用本次控制器、真实 electron-updater 和 NSIS 安装器，完成下载、退出替换、自动重启，保存的测试装配及备份保留。测试下载通过本机 HTTP 定向，不是对公网未来版本的声明。证据在 output/updater-e2e。曾发现依赖打包遗漏和测试环境变量在重启后丢失，均已修正；测试目录定位随测试包保存，不依赖继承环境变量。

生产包冒烟还验证 updater 版本、后端备份接口、原装配事务及沙盒导航。尚未声明对任意未来引擎来源变化自动迁移，也未实现跨平台更新或自动回滚已迁移数据。
