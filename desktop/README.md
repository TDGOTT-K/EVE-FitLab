# Windows 桌面构建

当前桌面壳为 Electron，后台为 PyInstaller 打包的 Python 服务，通过 stdio MCP 启动 NEngine。旧 Dogma/.NET 9 宿主已删除。

## 前置条件

- Windows x64、Node.js 22+、Python 3.11+、PyInstaller 6.22.3。
- 独立 NEngine 源码和固定基线：按引擎 README 准备 `.tools/dotnet`、`artifacts/sde-mutation-index-002`，构建 `src/NEngine.Mcp` 和 `src/NEngine.Cli` 的 Debug/net10.0 输出。
- 默认引擎位置为相邻 `../NEngine`，也可传 `-EngineSourceRoot`。打包只复制引擎，不修改其源码。

```powershell
npm ci
python -m pip install pyinstaller==6.22.3
powershell -NoProfile -ExecutionPolicy Bypass -File desktop/build.ps1 -EngineSourceRoot '..\NEngine'
```

`prepare_nengine.py` 会生成 `desktop/build/web`、`desktop/build/nengine` 和带版本/契约哈希的 `release-manifest.json`。暂存目录必须是新的空目录；已存在内容时脚本拒绝覆盖。再次打包前自行保存需要的产物并清理对应暂存目录。

输出默认在 `release/`。`package.json` 版本尚未为本次整理发布升级；发布前应确定新版本，不覆盖官网历史安装包。

## 运行与验证

数据、浏览器资料、日志分别位于 `%LOCALAPPDATA%/EVE-FitLab/` 下的 Data、Browser、Logs。后台使用随机回环端口与每次启动密钥；SSO 回调使用 5207。安装包不包含开发机用户状态。

```powershell
python desktop/verify_windows.py --app release/win-unpacked --output output/windows-verification-new
```

该命令测试实际桌面应用及打包 CLI。源码门禁不能替代桌面打包/安装验证、真实 SSO 和干净 Windows 环境验证。本次整理的实际测试结果见 `docs/workspace-cleanup.md`；不沿用旧 0.1.1/rc.4 的安装验收结论。
