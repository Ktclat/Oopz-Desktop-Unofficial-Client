# Oopz Desktop (Unofficial Client)

> 一个基于 [Tauri 2.0](https://tauri.app/) 构建的 Oopz 第三方桌面（套壳）客户端。旨在提供更纯净、更低资源占用的语音交流体验。

![Rust](https://img.shields.io/badge/Rust-backend-orange) ![Tauri](https://img.shields.io/badge/Tauri-2.0-blue) ![Platform](https://img.shields.io/badge/Platform-Windows-lightgrey)

## ✨ 主要特性 (Features)

相比于直接使用浏览器网页版，本客户端提供了以下原生级增强：

- **🛡️ 单实例运行 (Single Instance)**
  - 完美解决多开问题。再次点击图标时，自动唤醒并置顶已存在的窗口，防止语音频道冲突。
- **📥 托盘常驻 (System Tray)**
  - 点击关闭按钮不退出，而是自动隐藏至系统托盘，保持后台语音在线。
  - 左键单击托盘图标快速显示/隐藏窗口。
  - 右键菜单支持“显示窗口”与“彻底退出”。
- **🚀 极致轻量**
  - 基于 Rust 后端驱动，安装包体积仅约 10MB，运行时内存占用显著低于 Chrome/Edge 浏览器。
- **🔧 权限增强**
  - 针对 Tauri 2.0 严格的权限系统进行了深度适配，解决了插件权限加载问题。

## 🛠️ 本地构建指南 (Build Guide)

如果你想自行编译本项目，请确保你的环境满足以下要求：

### 前置环境
1. **Rust**: 请安装 [Rustup](https://rustup.rs/) (建议使用 MSVC 工具链)。
2. **Node.js**: 建议使用 LTS 版本。
3. **WiX Toolset v3**: 用于生成 Windows MSI 安装包。

### 1. 安装依赖
```bash
npm install
```

### 2. 开发模式运行
```bash
npm run tauri dev
```

### 3. 打包正式版
```bash
npm run tauri build
```

---

## ⚠️ 免责声明 (Disclaimer)
本项目为 Oopz 的第三方非官方客户端，仅供学习 Tauri 框架及个人使用。
* 本项目与 Oopz 官方无任何关联。
* 所有网页内容、Logo 及服务均归 Oopz 官方所有。
* 请勿用于商业用途。

## 📄 开源协议 (License)
* MIT