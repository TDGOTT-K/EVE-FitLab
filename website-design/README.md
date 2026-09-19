# FitLab 官网

四语言静态官网源文件。`download.html` 提供版本选择，第二个公开测试版为 0.1.2-rc.1（保留安装包版本号），历史版本保留 0.1.2-beta.1，默认展示预览版。安装文件托管在 GitHub Releases。

开发预览：`python -m http.server 5217 --directory website-design`。首页首屏按钮滚动到下载区，再由选择版本入口进入下载页。

发布时将本目录 HTML/CSS/JS 和 assets 同步到 website，保留 website/_headers，再使用 Wrangler 部署 website。更新版本必须同时更新下载 URL、日期、大小、SHA-256 和四语言说明。安装包发布验证通过后才能开放链接。
