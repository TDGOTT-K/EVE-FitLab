# Integration status — 2026-09-20 (in progress)

- Scope: zh-CN, zh-TW, en, ja, de, ru, fr. TW is OpenCC/community conversion, not official. User confirmed keep TW.
- Preserve unrelated untracked docs/phase1-definition-and-gates.md and website/assets/logo.png.
- Browser-only, no package/deploy/frozen release changes.
- Isolated app: http://127.0.0.1:56520 ; state under output/localization-20260920. Pinned runtime: NEngine 0.210.6-ui.repair1, SDE 3503375.
- Catalog: 6122 entries, en=6122; zh/ja/de/ru/fr each 6120. Static display metadata rebuilt from pinned SDE: 6535 selected types including mutaplasmids, 9256 legacy exact display terms. No substring game-name translation in the legacy matcher; ID markup introduced for hulls, modules, ammo, skills, fighters, implants and mutation materials.
- Locale resource baseline expanded 901 -> 1879. Semantic migration script prepared but must wait until Sol resources complete.
- Luna en 28 fixes, ja 33 initial fixes. Later batch output rejected: mixed-language substitutions and invalid ru/TW JSON. TW restored from HEAD then completed using OpenCC with user authorization. User authorized gpt-5.6-sol fallback.
- Active Sol: sol_en_de owns en/de/ru; sol_ja_fr owns ALL ja/fr (original /root/japanese was Luna and is idle). No other Japanese translator is active.
- 75 browser modules import/binding checks pass. Isolated r54 EFT/plan sharing Python tests: 10 pass. JS plan protocol, draft snapshots, panel details, valuation, capacitor summary and crystal identity pass.
- test_fit_image_code.mjs fails historical Active->Online expectation (line 33); relevant protocol files unchanged. Do not alter business state normalization to pass a localization task.
- Browser initial zh/TW/en/ja switching preserved entire localStorage except language and hash, mixed Chinese/Japanese/Russian draft name unchanged. Initial 1045x912 settings screenshot no overlap. 760 narrow view exposed pre-existing inspector grid misplacement; CSS adjusted and needs final screenshots.
- Acceptance NOT complete: active resources still being translated. Need final seven-language checks, dynamic panels, screenshots, source consistency and commit.
