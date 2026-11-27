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

## Windows 原生预览版

原生分支提供独立的 Windows x64 程序 `Oopz.Native.exe`。窗口、频道列表、消息和麦克风自检由 WPF 与 Windows 音频接口实现。解压便携包即可运行，需要 Windows 10/11 和 .NET Framework 4.8。[原生版发布记录](https://github.com/Ktclat/Oopz-Desktop-Unofficial-Client/releases/tag/native-v0.3.0-alpha.1)

- **已有本机 Oopz 适配器**。启动一个已获账号授权、已登录且同时启用 HTTP 与正向 WebSocket 的 OneBot v11 服务，点「连接适配器…」，输入本机端口和适配器令牌。默认端口为 6700。选中频道即可收发文字，Enter 发送。「断开连接」和关闭程序都会断开事件流。令牌仅在当前进程中使用。可见频道和发言身份由适配器账号决定。最近记录来自适配器的事件缓存。[适配器接口](https://github.com/tangqingfeng7/Oopzbot-SDK/blob/25df9629157ccdf11c540de1554acfd3a8c06314/docs/adapters/onebot/v11.md)
- **核对网页版接口数据**。在自己的官方网页会话中打开开发者工具的 Network 面板，再打开已加入的域和文字频道，选择导出经过净化且包含响应正文的 HAR。在原生客户端点「打开网页记录…」，可离线查看其中的域、频道和文字。只读取匹配的 Oopz API 响应，原文件留在本机。HAR 可能包含私人聊天，请仅在本机使用。此模式只读。
- **麦克风自检**。点「麦克风自检…」，选择设备并开始。可以观察输入电平，随时停止，10 秒后会自动释放输入设备。

**个人账号直接登录与语音**。公开的社区实现记录了登录请求签名和按房间取得语音授权的流程，公开构建使用占位材料。当前原生预览版提供适配器文字收发及本机音频测试。个人账号直登、Oopz 房间通话仍待可验证的接入授权及真实服务联调。[鸿蒙原生实现说明](https://github.com/real-LEONEAST/OopzHM/blob/54b1de9315b6402a66f63e80a8ce7b98da9b98cc/README.md)

Windows 安装 Visual Studio 的「.NET 桌面开发」工作负载和 .NET Framework 4.8 Targeting Pack 后，运行 `msbuild native-windows/Oopz.Native.csproj /p:Configuration=Release` 即可生成客户端。原生分支的 Actions 在 Windows 桌面测试启动、麦克风窗口、频道收发和关闭流程；原生预览标签采用 `native-v主.次.修订-alpha.序号`。

## ✨ 主要特性 (Features)

相比直接使用浏览器网页版，本客户端提供了以下**原生级增强体验**：

- **单实例**。再次启动时，恢复并聚焦已打开的窗口。
- **重新加载**。在窗口菜单中选择「重新加载」，或按 `Ctrl+R`。该操作会刷新当前网页，可能会中断正在进行的语音会话。
- **退出**。在窗口菜单中选择「退出 Oopz」，或按 `Ctrl+Q`，结束客户端并释放后台资源。
- **Windows 托盘**。关闭窗口会将客户端隐藏到托盘，后台语音可继续运行。单击托盘图标可隐藏或恢复窗口，右键菜单提供显示、隐藏、重新加载和退出。托盘初始化失败时，关闭窗口会直接退出。
- **Linux 窗口**。最小化可保留会话，关闭窗口会退出客户端。macOS 菜单快捷键使用 `⌘`。

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
