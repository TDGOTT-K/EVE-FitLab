# 凤凰导弹视觉链路样本

## 当前版本：原版材质与机械动画恢复

2026-09-20。以下旧记录描述前期临时模型；本节取代其中临时材质、整机旋转与包围盒发射点的状态。

- 已从原始weapon.black的57项字符串表恢复18个24字节常量记录的参数名，再调用原插件材质逻辑解析加达里阵营覆盖值。原始/解码证据为output/ship-assets-probe/chain/weapon.black、decoded-material.json。没有猜测字段名称。
- 恢复Albedo、Material、PaintMask、Roughness、Normal、Glow等原图与原参数，通过原插件Quad节点构建后烘焙到2K PBR。检查导入网格确有原始custom normals；法线贴图按Carbon节点的XY偏移及隐式Z语义烘焙。非完全等价Carbon shader。
- launcher-textured.glb为9,285,976字节，含1套骨骼皮肤和Active/Deploy/Fire/Inactive/Pack/Reload六段动作。保留原始顶点权重和法线，仅从烘焙模型移植同拓扑UV和材质。没有冻结或重捏模型。
- 播放使用原始Deploy/Fire/Pack，展示时间压缩为17.5–19、19–25、25–27秒；这不是原游戏动作时长/武器周期的承诺。未发现专门Yaw/Pitch骨骼，移除之前程序化整机硬转。暂停与倒退直接采样动画时间，不累计推进。
- 改用原始Pos_Fire01骨骼定位发射点，19秒采样时导弹起点与骨骼世界位置距离为0。只是单枚视觉事件，未接四口齐射及原始firingEffect。
- 移除0.48单位的任意缩放；保留导入模型原始单位，仅按插件规则消除挂点缩放。尚未与游戏内同镜头截图核准最终挂载姿态/比例。
- 六状态截图与自动验收在output/playwright/ship-assets/restored-*.png及chain/restored-qa-result.json。骨骼位移确实变化（LauncherLoader由40至50后回40）；暂停稳定、倒退同值、枪口匹配。glTF Validator 0错误、1条skinned mesh非根节点警告，浏览器已检查实际挂载与蒙皮播放；仍不能宣称跨所有渲染器零差异。
- 原六舰95.96MB加此发射器约105.25MB，远低于1GB。飞行导弹、尾迹与护盾仍为程序化样本。

恢复脚本保存在本目录tools/；依次恢复材质、SHIP_PROBE_ID=launcher运行scripts/export-ship-sample.py烘焙、导出rig。预览入口 http://127.0.0.1:8766/chain/?view=launcher 。

## 早期实验记录


访问 http://127.0.0.1:8766/chain/ 。开发服务器根目录为 `output/ship-assets-probe`。本目录保存预览源码；部署时将index.html、chain.js、timeline.js、event.json复制到该根下chain目录。依赖已有pack、tools/node及本地生成的chain/launcher.glb、launcher-source.json。

真实输入：sandbox-preview-data.js 的 missile-580，Phoenix→Bhaalgorn，19秒发射、24.2秒命中，事件678。仅展示单枚，不是完整齐射。事件摘录保留引擎版本、request hash和数据文件hash。

真实模型：typeID37294 XL Torpedo Launcher II，资源路径 `res:/dx9/model/turret/launcher/citadeltorpedo/citadeltorpedo_t1.black`，GR2已获取导出，GLB134,096字节。原模型包含动作，但本样本静态冻结；未声称舱门动作已经还原。原版材质constParameters由服务返回未解码StructureList，插件拒绝；采用临时金属材质，不猜测参数名。挂点使用凤凰locator_xl_1a的位置/旋转，消除挂点缩放，发射器样本尺寸校准为场景0.48单位，不宣称真实挂载比例。

导弹体、尾迹、炮口闪光、冲击球及护盾shader为程序化视觉素材；没有下载原版导弹/护盾shader。发射口暂由挂载模型包围盒上方构造；没有经过原版炮口socket验证。舰船距离、大小、曲线路径与命中点是展示布局，不是引擎弹道。护盾波纹用于展示可控效果，不能推断该次伤害由护盾吸收。

timeline.js 是绝对时间纯函数：所有瞬时效果均由time求值，无累计粒子状态，支持暂停/倒退/跳转。浏览器提供chainDemo.seek、play、pause、getState。场景连接真实发射/命中时刻，不产生或修改引擎伤害计算。

验证：Playwright实测飞行→命中→倒退到21秒，导弹坐标和状态逐项相同；暂停400ms无漂移；2倍速500ms推进约1秒；全景、挂点特写和目标特写均完成截图检查。结果在output/ship-assets-probe/chain/qa-result.json，截图在output/playwright/ship-assets/chain-*.png。浏览器无加载错误，只有原有环境shader浮点精度warning。

原六舰包95.96MB，发射器额外约0.134MB，程序效果无额外大型贴图。没有改正式沙盒或引擎。下一步要补原版发射器材质解码、尺寸/炮口及动画映射，再考虑多发射口和多舰事件池。

## 发射器转向修复

整机绕真实挂点作程序化旋转：17.5–19秒平滑对准目标，25–27秒复位。非原版转台/俯仰骨骼动画；枪口使用网格轴向端点近似。导弹起点在发射姿态取值，闪光与其一致。aim-qa已验证前中后姿态不同、暂停无漂移、倒退一致、复位一致，发射时枪口与弹体距离为0。预览参数`?view=launcher`默认特写。
