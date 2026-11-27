# Oopz Desktop (Unofficial Client)

<div align="center">

<!-- 二次元可爱访问计数 -->
![Moe Counter](https://count.getloli.com/get/@Ktclat-Oopz-Desktop?theme=moebooru)

</div>

> 一个基于 [Tauri 2.0](https://tauri.app/) 构建的 Oopz 第三方桌面（套壳）客户端。  
> 旨在提供更纯净、更低资源占用的语音交流体验。

![Rust](https://img.shields.io/badge/Rust-backend-orange)
![Tauri](https://img.shields.io/badge/Tauri-2.0-blue)
![Platform](https://img.shields.io/badge/Platform-Windows-lightgrey)

---

## ✨ 主要特性 (Features)

相比直接使用浏览器网页版，本客户端提供了以下**原生级增强体验**：

- **单实例**。再次启动时，恢复并聚焦已打开的窗口。
- **重新加载**。在窗口菜单中选择「重新加载」，或按 `Ctrl+R`。该操作会刷新当前网页，可能会中断正在进行的语音会话。
- **退出**。在窗口菜单中选择「退出 Oopz」，或按 `Ctrl+Q`，结束客户端并释放后台资源。
- **Windows 托盘**。关闭窗口会将客户端隐藏到托盘，后台语音可继续运行。单击托盘图标可隐藏或恢复窗口，右键菜单提供显示、隐藏、重新加载和退出。托盘初始化失败时，关闭窗口会直接退出。
- **Linux 窗口**。最小化可保留会话，关闭窗口会退出客户端。macOS 菜单快捷键使用 `⌘`。

### Windows 原生托盘待机

- **需要接收消息或继续通话时**，点击窗口关闭按钮即可隐藏到托盘。网页会保持运行。
- **暂时不用 Oopz 时**，在托盘菜单选择「断开会话并卸载网页」。客户端销毁网页窗口，当前通话结束，待机期间收不到官网消息。之后点「打开 Oopz」或再次启动程序，会重新加载官网。登录状态由官网管理。
- **希望启动时先待机时**，勾选托盘菜单中的「启动时仅显示托盘图标」。也可以用 `oopz.exe --tray-only` 临时启用一次。这个模式只创建原生托盘，仍可从「系统声音」检查 Windows 的输入设备与权限。默认启动会直接打开 Oopz。

### 页面与语音

客户端直接加载 `https://web.oopz.cn/`。官网发布新版本后，下次加载即可使用。以下页面功能只在这个域名的主页面生效。

| 操作 | 使用方法 |
| --- | --- |
| 精简图片 | 选择「页面 → 选择要隐藏的图片」，点选独立图片，或用 Tab 选择并按 Enter 确认。可用 Esc 取消。相同地址的图片会一起隐藏，按钮、链接和媒体控件中的图片会保留。选择「恢复已隐藏的图片」可撤销。 |
| 通话前检查输入 | 选择「语音 → 麦克风自检」，选好测试设备后点击「开始测试」。观察输入电平和 WebView 报告的处理状态。10 秒后自动释放测试麦克风，点「停止测试」或关闭面板也可提前结束。通话设备仍在官网中选择。 |
| 调整拾音处理 | 「语音 → 切换降噪与回声消除」默认关闭。开启后，下次官网请求麦克风时会优先使用 WebView 支持的降噪和回声消除。官网明确指定的参数优先。切换时当前通话继续使用原来的参数。 |
| 排查 Windows 输入问题 | 从「语音」菜单打开「系统声音设置」检查设备和输入音量，或打开「系统麦克风权限」检查桌面应用的访问权限。 |

图片选择和语音偏好保存在本机。含查询参数或片段标识的图片地址仅保留至本次页面关闭或刷新。图片精简改变显示方式，官网仍可能下载这些图片。

Windows 隐藏到托盘后，客户端会通知 WebView2 停止显示页面，并在支持的版本上请求降低内存使用目标。恢复窗口时重新显示页面并切回正常目标。此操作保留网页、脚本和网络连接。实际内存变化由 WebView2 根据页面活动和系统内存压力决定。

## 🛠️ 本地构建指南 (Build Guide)

如果你希望自行编译或参与开发，请确保本地环境满足以下要求。

### 前置环境 (Requirements)

1. **Rust**
   - 安装方式：[Rustup](https://rustup.rs/)
   - 建议使用 **MSVC 工具链**

2. **Node.js**
   - 建议使用 **LTS 版本**

3. **WiX Toolset v3**
   - 用于生成 Windows 平台的 MSI 安装包

---

### 1️⃣ 安装依赖
```bash
npm ci
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
### Linux 开发

在 Debian 13 上安装 Tauri 的原生依赖后，从仓库根目录执行开发命令。

```bash
sudo apt-get install build-essential pkg-config libwebkit2gtk-4.1-dev
npm ci
npm run tauri dev
```

验证 Rust 编译可运行 `cargo build --locked --manifest-path src-tauri/Cargo.toml`。启动桌面窗口需要图形会话。

### Windows 预览构建

向 `preview/**` 分支推送代码会触发 **Windows preview**。便携包放在该次运行的 Artifacts 中。工作流在有 Explorer 桌面的 runner 上检查托盘启动、WebView2 子进程和再次启动唤醒窗口的行为，并在没有可用桌面时标记跳过。

### 通过 GitHub Actions 发布

稳定版沿用 `v主版本.次版本.修订号` 标签和 `Oopz Desktop v主版本.次版本.修订号` 标题。发布前同步 npm、Cargo 和 Tauri 的版本及锁文件，在 `releases/<标签>.md` 写好中文更新说明，再将提交及对应标签推送到 GitHub。

标签触发 **Windows release** 工作流。Windows x64 runner 运行测试、生成 NSIS 和 MSI 安装包，并打包便携 ZIP。工作流上传全部文件后公开 Release。失败时可在 Actions 页面重新运行该次任务，也可选择 **Run workflow** 并填写已有标签。更新发布工作流时也会核对当前版本的标签。所有构建均检出该标签对应的提交。

## ⚠️ 免责声明 (Disclaimer) 
本项目为 Oopz 的第三方非官方客户端，仅供学习 Tauri 框架及个人使用。 
* 本项目与 Oopz 官方无任何关联。
* 所有网页内容、Logo 及服务均归 Oopz 官方所有。
* 请勿用于商业用途。

## 📄 开源协议 (License) 
* MIT
