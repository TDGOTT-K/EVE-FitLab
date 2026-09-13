# Windows 桌面版

Windows 10 / 11 x64；安装包自带 Electron、Python 和 .NET 运行时以及 Dogma 所需的静态数据。用户不需要 Node、Python、.NET SDK 或旧项目目录。

运行 `release/EVE-FitLab-0.1.1-Windows-x64-Setup.exe`，选择安装目录。安装器提供开始菜单、桌面快捷方式和 Windows 卸载入口。卸载保留用户数据。

## 数据和后台

- 默认数据目录：`%LOCALAPPDATA%/EVE-FitLab/Data`，可在应用设置迁移。
- 浏览器持久化资料：`%LOCALAPPDATA%/EVE-FitLab/Browser`，固定的内部 `fitlab://app` 来源保存草稿与文件夹。
- 启动日志：`%LOCALAPPDATA%/EVE-FitLab/Logs`。
- 后台服务绑定随机回环端口，以每次启动生成的密钥验证请求；关闭应用同时关闭两个后台进程。
- 安装包不包含开发机的装配、角色、state、SSO 配置和授权令牌。
- 程序使用隔离渲染环境，关闭 Node 集成、浏览器菜单和开发者快捷键。网站、邮箱链接交给系统浏览器/邮件应用。

## 官网授权

已内置 EVE FitLab 原生应用 Client ID，用户直接登录授权，无需注册开发者应用。回调地址仍为 `http://127.0.0.1:5207/api/eve/callback`。桌面版只在授权期间占用回调端口；若旧网页开发服务占用 5207，需先关闭它。用户在系统浏览器完成登录后返回桌面程序。

尚未使用真实账号验证整个 SSO 外部链路。桌面启动、真实 Dogma 计算和后台鉴权通过独立程序验证。

## 构建

构建机需要 Node.js/npm、Python 3.9+、PyInstaller，以及 .NET 9 SDK；用户机器不需要。

```
npm ci
python -m pip install pyinstaller==6.22.3
powershell -NoProfile -ExecutionPolicy Bypass -File desktop/build.ps1 -SdeRoot 'D:/EVE-SDE/eve-online-static-data-3248221-jsonl'
```

引擎源码默认使用仓库内的 `engine/`，可用 `-EngineSourceRoot` 覆盖。SDE 路径必须明确指定。最终安装包完全自包含；C# 主机只公开装配导入与计算接口，不运行旧项目的其他业务服务。

`--smoke-test` 启动参数执行全技能 V 裂谷级真实计算后退出，输出报告和截图。可设置 `FITLAB_TEST_ROOT` 将验收资料隔离到临时目录。

## 发布状态

0.1.1 为本地可安装试用版本，目前未进行代码签名。Windows 可能显示未知发布者或 SmartScreen 提示；正式公开发布需配置代码签名证书并验证干净的 Windows 虚拟机。尚未配置自动更新服务器。

静态数据和游戏图标来自 CCP EVE Online；本工具由深海的鱼（ImFishMan）开发，与 CCP 无隶属关系。运行时组件的许可证随 Electron/.NET/Python 发行文件保留。


## 本次安装验收

安装器已在当前 Windows 主机完成实际安装、安装后启动、全技能 V 裂谷级计算与静默卸载。CPU 为 162.5，未带会话密钥访问后台返回 403，卸载返回 0，程序文件已移除，用户数据仍保留。42 项 Python 回归测试通过。验收记录位于 `output/desktop-install-result.json`。

这不是干净虚拟机或其他 Windows 版本的兼容性认证；正式发行前仍应执行跨机器测试和代码签名。
