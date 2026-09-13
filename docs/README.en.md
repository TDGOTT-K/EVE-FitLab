<p align="center"><img src="images/banner.svg" width="100%" alt="EVE FitLab"></p>

# EVE FitLab

**Get your fit right before you undock.** A Windows fitting workspace for EVE Online: configure ships, simulate performance, manage characters and share importable fitting images.

[Website & downloads](https://imfishman.com/) · [Support development](../SPONSOR.md) · [Report an issue](https://github.com/TDGOTT-K/EVE-FitLab/issues) · [简体中文](../README.md)

> The first public beta **0.1.2-beta.1** is available. [Choose a version](https://imfishman.com/download) or [view the release](https://github.com/TDGOTT-K/EVE-FitLab/releases/tag/v0.1.2-beta.1). This is an unsigned test release; keep backups of important fits.

## 💛 Tokens are expensive… spare a little fuel?

<p align="center"><img src="images/please-feed-me.svg" width="150" alt="Please feed the developer"></p>

I’m **ImFishMan**. If FitLab helps you, consider supporting its AI-assisted development and maintenance. **USDT · TRON (TRC20)**: `TYDiRLFWukWdHpiZKQdGLoX7ivH2PFtPBS`.

**[QR code and donation details →](../SPONSOR.md)**. Donations are optional, with no feature or delivery commitments. Stars, translations, bug reports and sharing with your fleet are welcome too!

## Features

- Drag-and-drop fitting, rack actions, multiselection, drones, cargo and T3 subsystems.
- Resources, DPS, defenses, capacitor calculations and visual target setup.
- Character skills through EVE SSO or custom skill profiles and folders.
- Ship tree, tags, notes, estimated prices and fit duplication.
- Detailed share images with fitting QR codes for local re-import.
- Simplified Chinese, Traditional Chinese, English and Japanese interfaces.

<img src="images/fitting.webp" width="100%" alt="Actual fitting workspace, Chinese interface">

## Development

Requires Python 3.9+, Node.js/npm and .NET 9 SDK. Clone the repository, run `npm ci`, download the official EVE **JSONL SDE**, and set `FITLAB_SDE_ROOT` to its extracted directory. The generated catalogs currently correspond to SDE build **3248221**. Run `python run.py`, then open `http://127.0.0.1:5207/`. Ports 5207 and 5210 must be free.

Required calculation engine sources are included under `engine/`; no private repository is needed. Full SDE is a separate large CCP data input. See the [main README](../README.md) for checks and [desktop documentation](../desktop/README.md) for packaging. The four-language website design is in `website-design/`.

**Fighters (carrier fitting) and Abyssal modules are not supported yet.**

The project is under active development: not a complete time-based combat simulator; real-account SSO verification, desktop code signing and automatic updates remain outstanding. Importing images requires a readable, complete fitting QR code.

## Contact and license

**深海的鱼 / ImFishMan** · wzx2377951590@gmail.com · QQ 2377951590 · Built with GPT-6-Astra.

Original code: [MIT](../LICENSE). CCP game data/artwork and third-party components retain their own rights; see [notices](../THIRD_PARTY_NOTICES.md). Independent fan project, not affiliated with CCP Games.
