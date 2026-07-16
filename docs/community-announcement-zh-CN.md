# HubRoute 中文发布稿

## 推荐标题

Unity Hub 国际版下载与启动工具：HubRoute v0.1.1

## 正文

HubRoute 是一款轻量、开源、跨平台的 Unity Hub 工具，可在默认浏览器中打开与当前操作系统、CPU 架构匹配的国际版 Unity Hub 官方下载地址，并通过已有本地 HTTP 代理启动 Hub。

由 HubRoute 启动 Unity Hub 后，再从 Hub 打开的 Unity Editor、Unity Package Manager 及其调用的 Git HTTPS 进程通常会继承相同代理，可改善 Unity 国际服务连接、编辑器下载以及 UPM 包和 Git 依赖拉取体验。

主要功能：

- 自动读取 Windows、macOS 和 Linux 的显式系统代理。
- 探测 Clash、Mihomo、V2Ray 等工具常见的本地 HTTP 代理端口。
- 支持手动代理、自动代理和直连三种模式。
- 在默认浏览器中打开 Windows 或 macOS 国际版 Unity Hub 官方下载地址。
- 代理环境仅注入 Unity Hub 子进程树，不修改系统全局代理设置。

项目地址：<https://github.com/HinataYoki/Unity_HubRoute>

最新版下载：<https://github.com/HinataYoki/Unity_HubRoute/releases/latest>

当前提供 Windows x64/ARM64、macOS x64/Apple Silicon、Linux x64/ARM64 自包含版本。其中 Windows x64 已验证，其他平台仍需要更多实机和桌面环境验证。

需要注意，HubRoute 不是代理软件，不提供代理节点、订阅、规则或网络隧道。使用自动或手动代理模式前，需要确保本地代理软件或企业 HTTP 代理已经可用。HubRoute 自身暂未进行商业代码签名，Windows SmartScreen 或 macOS Gatekeeper 可能显示未知开发者提示，请只从项目 Releases 页面获取发布包并核对来源。

## 短版

HubRoute v0.1.1 已发布：支持在默认浏览器中打开国际版 Unity Hub 官方下载地址，并通过已有本地 HTTP 代理启动 Hub。由 Hub 打开的 Unity Editor、UPM 和 Git HTTPS 通常也会继承相同代理。提供 Windows、macOS、Linux 自包含版本，不修改系统全局代理，也不提供代理节点。

项目与下载：<https://github.com/HinataYoki/Unity_HubRoute>

## 视频简介

HubRoute 可以在默认浏览器中打开国际版 Unity Hub 官方下载地址，并通过已有的 Clash、Mihomo、V2Ray 或企业 HTTP 代理启动 Hub。启动后的 Unity Editor、Unity Package Manager 和 Git HTTPS 通常会继承代理环境。工具支持 Windows、macOS 和 Linux，不修改系统全局代理设置，也不包含代理节点或订阅。
