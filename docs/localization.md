# 本地化

支持 zh-CN、zh-TW、en、ja。设置中的显示语言即时生效并保存到 fitlab-language；首次参考 navigator.language。切换不重新加载应用、不更改装配数据，保留输入、未保存修改及滚动位置。

## 文件

- locales/source.json：898 条简体中文源消息（采用源消息作为键）。
- locales/en.json、zh-TW.json、ja.json：相同键集合的译文。英语和日语使用 Luna 初稿，主代理做术语/句子校正；繁中由 OpenCC 转换为初稿。
- locale-overrides.js：少量尚未独立进入源消息清单的补充词条。
- i18n.js：t/translateFor、语言偏好、Intl 格式化、游戏名称查询、跨语言搜索，以及旧 UI 的显示层迁移适配器。
- data/locale-game.json：6048 个物品/舰船/技能的 SDE 多语名称，另含市场分类、属性和分组词条。英语/日语缺失时回退英语；繁中由简中转换，并非官方独立繁中数据。
- locale-vendor.js：随应用分发的 OpenCC 转换器，无运行时 CDN 依赖。许可证在 assets/vendor/。

## 迁移策略

旧 UI 的中文标签还承担少数对象键/分组比较职责。显示层适配器保存文本节点/显示属性的原文，仅更新可见译文，不修改模型、data-* 属性或事件处理。切换时从保存的源消息重译，不从上一种译文反推。观察后续新增节点与属性，覆盖菜单、弹窗、状态与错误提示。用户名称、标签、备注、输入值及自定义文件夹需标记 translate=no/data-no-i18n 或使用适配器保护的控件。不要依赖翻译后的 textContent 作为业务键。

新增功能应显式调用 t('完整源消息',{name:...})，参数用 {name} 占位符，不拼接需要语序变化的句子，并同步增加四种语言词条。HTML 用户值仍需转义；translate=no 不是 HTML 转义器。现有动态片段保留原行为，后续可按功能逐步迁移到完整消息模板。

分享图使用 translateFor(imageLocale,source)，在绘制前翻译和测量文字；用户构成名称、标签和备注保持原文。导出语言独立选择，二维码的二进制装配数据不变。英文/日文信息窗口优先选择元数据的相应语言，缺失则用英文。

## 构建与验证

- npm run check:locales：检查所有源键存在、无空译文。
- npm run build:locale-vendor：重新打包 OpenCC。
- python scripts/build-game-locales.py：从开发机 SDE 更新官方名称。
- desktop/prepare.py 已包含语言 JSON 和 locale-game.json；本次没有生成安装包。

浏览器验收：四语言即时切换、持久化、字符/文件夹管理、官方日文物品搜索；草稿和 hash/滚动不变；用户内容保护；英文导航在 920px 无重叠；英/繁中/日文 PNG 导出；本地化图片扫码还原 505 项技能。17 项现有服务/角色测试通过，原二进制分享协议测试通过。尚未重新验证打包后的 Windows 应用。
