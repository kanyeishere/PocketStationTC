<p align="center">
  <img src="frontend/public/logo.svg" alt="Pocket Station" width="180">
</p>

<h1 align="center">Pocket Station</h1>
<p align="center"><strong>把手机变成 FFXIV 的第二块屏幕 · Turn your phone into a second screen for FFXIV</strong></p>

---

## 📖 简介 | Introduction

**Pocket Station** 是一个 FFXIV Dalamud 插件，在你的游戏内运行一个局域网 Web 服务器。你可以用手机、平板或笔记本电脑连接上来，实时查看游戏聊天、角色状态、发送指令、直播游戏画面，甚至让手机成为你 FFXIV 自动化的控制终端。

**Pocket Station** runs a local LAN web server inside FFXIV. Connect from any phone, tablet, or laptop on the same network to monitor game chat, view player state, send commands, capture screenshots, or stream your game screen live — like having a handheld console in your pocket.

---

## ✨ 功能 | Features

| 功能 Feature | 说明 Description |
|---|---|
| 💬 **聊天监控** Chat Monitor | 实时查看游戏内聊天消息，支持按频道类型过滤、关键词包含/排除，可保存自定义过滤模式 |
| 🧑 **角色状态** Player State | 查看自身 HP/MP、职业、等级、Buff/Debuff、坐标、当前目标、小队成员，以及所在服务器/大区/地图 |
| 💰 **货币追踪** Currency Info | 显示神典石、美学神典石等各类货币余额 |
| 📡 **实时画面直播** Live Stream | 以可调帧率（1–120 FPS）将游戏画面实时推流到移动设备 |
| ⌨️ **指令快捷方式** Command Shortcuts | 预设常用指令按钮（如自动排本、收取潜水艇），一键从手机发送到游戏执行 |
| 🔌 **插件管理** Plugin Manager | 查看和开关已安装的 Dalamud 插件 |
| 🤖 **DailyRoutines 集成** | 浏览和开关 DailyRoutines 自动化模块 |
| 📋 **自定义指令** Commands | 在手机上输入任意指令发送到游戏执行 |
| 🔐 **Token 鉴权** Auth | 支持 Token 认证，保护局域网访问安全 |

---

## 🚀 快速开始 | Quick Start


0. 添加裤链 `https://github.com/kanyeishere/PocketStation/releases/latest/download/pluginmaster.json`
1. 在 Dalamud 插件中心安装 **Pocket Station**
2. 打开插件设置 (`/ps` 或 `/pocketstation`)
3. 确认 **Enable LAN server** 已开启，记下显示的 URL（例如 `http://192.168.1.5:8787`）
4. 在手机/平板的浏览器中输入该 URL，并附带 Token 参数（URL 中已包含）
5. 开始使用！

> 💡 第一次连接后 Token 会保存在浏览器本地存储中，下次自动带上。

---

## 🌐 远程访问 | Remote Access

你不仅可以在家里用——通过以下方式，出门在外也能连回家里的游戏：

### 方式一：Tailscale（推荐）

1. 在电脑和手机上分别安装 [Tailscale](https://tailscale.com/)
2. 两台设备登录同一个 Tailscale 账号
3. 手机浏览器直接访问电脑的 Tailscale IP（如 `http://100.x.x.x:8787`）
4. ✅ 加密传输，无需公网 IP，开箱即用

### 方式二：路由器端口转发

1. 在路由器中设置端口转发：将公网端口（如 `8787`）转发到电脑的内网 IP
2. （推荐）同时开启 **Require token** 保证安全
3. 手机通过公网 IP 或 DDNS 域名访问（如 `http://your-ddns.example.com:8787`）
4. ⚠️ 建议搭配 HTTPS 反向代理（Nginx/Caddy）使用以加密传输

### 方式三：内网穿透工具

使用 frp、ZeroTier、Cloudflare Tunnel 等工具实现安全的远程访问。

---

## 🛠️ 配置 | Configuration

| 设置 | 默认值 | 说明 |
|---|---|---|
| **Port** | `8787` | 局域网 Web 服务端口（1024–65535） |
| **Require token** | `true` | 是否要求 Token 认证 |
| **Max clients** | `8` | 最大同时连接客户端数 |
| **Chat history limit** | `500` | 聊天历史缓存条数 |
| **Player state interval** | `750ms` | 角色状态推送间隔 |
| **Screenshot quality** | `75` | 截图 JPEG 质量（20–95） |
| **Stream FPS** | `30` | 直播推流帧率（1–120） |

---

## 🏗️ 技术栈 | Tech Stack

- **后端**: C# / .NET (Dalamud Plugin) — 内嵌 HTTP + WebSocket 服务器
- **前端**: Vue 3 + TypeScript + Vite — 响应式移动优先 SPA
- **通信**: REST API + WebSocket 实时推送 + 二进制帧流

---

## 📄 许可 | License

MIT

---

<p align="center">
  <sub>Made with ❤️ by Wotou · <a href="https://github.com/kanyeishere/PocketStation">GitHub</a></sub>
</p>
