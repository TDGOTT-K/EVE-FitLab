# 官网公告

客户端读取 `https://imfishman.com/announcements/feed.json`。Cloudflare Pages 静态文件即可，不需要数据库、WebSocket 或额外网站。启动获取一次；设置 → 公告 → 查看公告会重新获取。网络错误不阻挡启动。

部署 `website/announcements/feed.json` 和 `website/_headers` 后生效。公告路径必须允许跨域并禁用缓存。当前附带欢迎公告，不宣称 v0.1.3 已发布；更新公告应在安装包、签名更新清单均上线后添加。

```json
{
  "schemaVersion": 1,
  "announcements": [{
    "id": "release-0.1.4",
    "kind": "update",
    "version": "0.1.4",
    "title": "FitLab v0.1.4 更新",
    "body": "更新说明正文，支持换行，不支持 HTML。",
    "publishedAt": "2026-10-01T00:00:00Z",
    "expiresAt": "2026-12-01T00:00:00Z",
    "startup": true
  }]
}
```

- 普通公告使用 `kind: "info"`，无需 `version`。`minVersion` / `maxVersion` 可限制受众，均为包含边界。生产更新公告应使用正式版本号。
- `publishedAt` 控制定时上线，`expiresAt` 可选，过期后隐藏。
- `startup: true` 的未读公告在启动进入装配库时弹出。若已有弹窗或已进入编辑页面，则保留在公告中心，避免抢占操作。启动请求最久等待 8 秒。
- 关闭公告后按 ID 记录在本机 localStorage；更新正文不会再次弹出，需要重新通知时使用新 ID。清除浏览器数据会清除已读记录。
- 更新公告仅向低于目标版本的客户端显示。“立即更新”关闭公告、打开设置并滚动到应用更新，自动检查并下载。安装仍需点击“保存并重启更新”，走已有保存、备份、签名和哈希校验。
- 公告不提供安装包 URL，也不执行脚本或远程 HTML。公告中声明的版本不会绕过签名更新清单；实际下载的是官网签名清单里的最新兼容版本。
- 浏览器与桌面共用 UI。浏览器点击“立即更新”仅定位更新面板，不下载或安装。

验证：`node test_announcements.mjs`、`node test_desktop_updates.cjs`、`node scripts/check-ui.cjs`。浏览器测试使用拦截官网请求的公告 fixture，不能当作官网已经部署的证据。

## 2026-09-22 接入验收

- 官网部署：`https://1ace4502.eve-fitlab.pages.dev`。生产公告源返回 200、`Access-Control-Allow-Origin: *`、`Cache-Control: no-store`；内容 SHA256 与本地 feed 一致。
- 发布前核对现有站点 22 个文件，除首页的 Cloudflare 邮箱保护自动改写外均与线上一致。部署上传仅新增 1 个公告文件及响应头，保留既有下载版本。
- 无请求拦截的浏览器会话 `announcements-live` 成功显示欢迎开屏公告。
- fixture 验证：已读持久化、重看公告、远程正文仅作文本、浏览器跳转、桌面 API 检查后下载且不自动安装。桌面联动此项为 API 模拟，未重新打包或声称完成真实新版安装验收。
- App 打包仍暂停；已安装的旧版需要升级到包含公告模块的版本后才能接收公告。
