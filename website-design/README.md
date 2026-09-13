# FitLab 官网设计稿

独立静态站点，预览：`python -m http.server 5217 --directory website-design`。

未发布，不改动 `website/` 的链路测试页。下载按钮当前禁用，明确标记发布准备中。发布时需接入经过验证的新版 Windows 安装包链接，并更新下载区版本号；不要直接使用旧安装包作为最新版本。

部署只需本目录 index.html、style.css、site.js 和 assets/，无需应用服务。图片均为本地素材，不依赖第三方图片接口。
