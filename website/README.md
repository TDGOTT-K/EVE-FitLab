# FitLab 官网

这是官网唯一源码；原 `website-design/` 与此处网页资源逐字节一致，重复副本已删除。保留 `_headers` 与历史下载元数据。

```powershell
python -m http.server 5217 --directory website
```

历史 0.1.2-rc.4 等安装包不代表当前 NEngine 开发源码已经发布。发布新版本时核对下载 URL、日期、大小、SHA-256 与四语言文案，再执行既有部署流程。本次整理未上传安装包或部署官网。

官网安装包按钮必须使用 https://imfishman.com/downloads/ 下的 Cloudflare 地址，历史版本同样适用。发布前运行 node scripts/check-website-downloads.cjs；下载服务说明见 infrastructure/downloads/README.md。
