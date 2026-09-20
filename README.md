# EVE FitLab

EVE Online 舰船装配工作台：浏览器界面、Python 本地服务、Electron Windows 桌面壳，以及通过公开 MCP 调用的独立 **[NEngine](https://github.com/TDGOTT-K/NEngine)**。

这是当前开发源码，不是官网旧测试安装包的发布说明。`package.json` 保留原有 `0.1.2-rc.2` 标识，不能据此判断当前引擎版本，也不表示本次整理已发布安装包。

## 当前架构与能力

- 当前接入基线：**NEngine 0.211.0-ui.workbench1 / 公共契约 r59 / 38 工具**，SDE **3503375**，静态规则 **eve-static-dogma-v59**。
- `nengine_bridge.py` 校验独立副本的 `UI-LOCAL-BASELINE.json`（优先）或 `UI-BASELINE.json`；不得跳过绑定校验或自动升级引擎。
- UI 包含装配库、角色技能、植入体/增效剂、深渊装备、无人机/舰载机库存、属性与电容视图、估价和装配图片分享。具体可用范围与诊断以引擎公开回执为准。
- 语言资源包含简中、繁中、英语、日语、德语、法语、俄语；资源齐全不代表所有翻译已经人工审校。
- 旧 `engine/` Dogma 源码和 `desktop/engine/` .NET 9 宿主已移除，不再提供 `FITLAB_CALCULATOR=legacy`。游戏数值由 NEngine 提供，不在计算失败时回退到旧引擎。

## 本地启动

推荐 Windows、Python 3.11+、Node.js 22+。引擎构建使用固定 .NET SDK **10.0.401**。把两个源码仓库放在同一父目录：

```text
workspace/
  EVE-FitLab/
  NEngine/
```

分别克隆两个独立仓库，并将引擎固定到本次已验证源码：

```powershell
git clone https://github.com/TDGOTT-K/EVE-FitLab.git
git clone https://github.com/TDGOTT-K/NEngine.git
git -C NEngine checkout d5a5066a4a21a18910ffda4f9d15d3d11c15bc89
```

引擎来源记录见 [固定源码与契约](docs/nengine-source.json)。NEngine 源码不放进 FitLab 仓库。

先按照 [NEngine 的 README](https://github.com/TDGOTT-K/NEngine#readme) 准备 SDK、固定 SDE 索引并构建 MCP/CLI；仓库源码不包含这些大型运行产物。本次整理的本地副本已附带被 Git 忽略的独立运行时和数据，未复制任何用户会话。

```powershell
cd EVE-FitLab
npm ci
powershell -NoProfile -File scripts/start.ps1
```

打开 **http://127.0.0.1:5208/**。直接运行 `python run.py` 也可以；`start.ps1` 额外把 UI 数据、引擎状态和目录配置固定隔离在本仓库 `state/` 中。

自定义位置与端口：

```powershell
powershell -NoProfile -File scripts/start.ps1 -EngineRoot 'D:\work\NEngine' -Port 5208
```

SSO 回调仍使用 `127.0.0.1:5207`。端口占用会明确报告；账号授权链路不能用普通单元测试代替真实账号验收。

## 开发与验证

```powershell
npm run check:ui
npm run check:locales
npm run test:i18n
npm run test:share-code
python -m pip install -r requirements-dev.txt
python scripts/check-phase1.py
```

前四项不需要运行引擎。阶段门禁需要完整独立引擎和固定 SDE，会使用临时状态并将日志写入 `output/`。它不是全机制正确性或安装包发布认证。

- [文档索引与目录职责](docs/README.md)
- [Windows 打包](desktop/README.md)
- [整理范围和删除清单](docs/workspace-cleanup.md)
- [现有能力与验收边界](docs/engine-v2/phase1-release.md)
- [贡献指南](CONTRIBUTING.md) · [安全说明](SECURITY.md) · [English](docs/README.en.md)

官网唯一源码位于 `website/`，预览：`python -m http.server 5217 --directory website`。官网保留历史已发布安装包信息，不应把它当作本分支的发布记录。

## 边界

这是测试阶段的软件。沙盒预览与 `experiments/` 属于研究材料；静态装配结果不等于完整战斗模拟。缺失、不适用、部分合计和来源信息须保留，不把未知数值填零。详细历史验收文档记载的是当时版本，不能累计成当前全量通过声明。

## 作者与许可

深海的鱼 · EVE ID **ImFishMan** · [官网](https://imfishman.com/) · [支持开发](SPONSOR.md)

原创代码采用 [MIT](LICENSE)；CCP 游戏数据与图像、第三方组件分别遵循其权利和许可，见 [第三方声明](THIRD_PARTY_NOTICES.md)。本项目与 CCP Games 无隶属或背书关系。
