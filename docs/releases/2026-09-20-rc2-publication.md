# 第三个公开测试版发布记录

- 日期：2026-09-20（北京时间）。版本：0.1.2-rc.2。
- 修复：启动时界面加载时间过长，舰载机目录加载不再阻塞主界面初始化。
- 使用用户指定安装包，未重新打包。包大小 218304947 字节，208.2 MiB。
- SHA-256：`5defb471e8fa8d9ace6ab80c5207ad643bf37a0411924d81ab1d6e59c52114ce`。
- GitHub Release：https://github.com/TDGOTT-K/EVE-FitLab/releases/tag/v0.1.2-rc.2；ID 392160988，prerelease。
- 标签对应冻结 UI：a6f54df0e468193fad802122eca5f9e63a26e074。
- 官网：https://imfishman.com/download#preview
- Pages 生产部署：https://a181c00f.eve-fitlab.pages.dev

本地冻结校验、上传返回 digest、匿名公网完整下载的 SHA-256 均一致。公网安装包 HTTP 200。线上首页和下载页已核对第三版版本号与修复说明；浏览器确认下载 URL 正确。四语言文案已更新，可滚动历史目录按顺序保留 rc.1 与 beta.1。

12.86 秒至 4.17 秒的启动改善引用随包 validation.json 的同机单次测量，并非本次发布重新测量或跨机保证。未追加宣称真实 SSO 或干净系统安装/卸载验证。原候选目录“只做本地候选，不发布”描述保留为冻结时记录，公开状态以本记录为准。

下载校验证据：output/third-beta-download-verification.json。发布目录仅包含 Git 跟踪的网站公开文件。
