# NovaLauncher

一款开源的 **Minecraft 第三方启动器**，使用 **C# / .NET 10 + Avalonia UI** 开发，原生跨平台支持 **Windows / macOS / Linux**。

深色主题，MVVM 架构，完整覆盖启动器核心能力。

---

## ✨ 功能特性

| 模块 | 说明 |
|------|------|
| 🚀 游戏启动 | 版本元数据解析、库规则过滤、Java 自动检测、classpath 组装、进程启动与日志捕获 |
| 🎮 账号 | 离线登录（昵称+UUID 生成）；微软正版登录（Device Code 全流程，需自行配置 ClientId）；**第三方外置登录**（authlib-injector，支持 LittleSkin 等皮肤站） |
| ⬇️ 版本下载 | 官方版本清单获取、客户端 jar / 全部库 / 资源文件并发下载、SHA-1 校验 |
| 🧩 加载器 | **Fabric** 一键安装（Meta API）；**Forge** Installer 下载与引导安装 |
| 📦 模组管理 | mods 目录文件列表、启用/禁用（.disabled）、删除、打开文件夹 |
| 🔍 模组下载 | 「模组下载」中心：接入 **Modrinth**（免费无需 Key）+ **CurseForge**（可选 Key）双数据源，搜索/按版本与加载器筛选/下载到指定版本 |
| 🎨 光影/资源包 | 模组下载中心支持**类型切换**（模组/光影/资源包），下载到对应 mods / shaderpacks / resourcepacks 目录 |
| ☕ Java 下载 | 自动检测所需 Java，从 **Adoptium (Eclipse Temurin)** 下载并内置 JDK（17/21 等），启动时自动补齐 |
| 🌐 全版本下载 | 版本管理页展示官方清单**全部**版本（含快照/旧版/远古版），支持搜索并一键安装任意版本 |
| 🗂️ 整合包 | NovaPack 格式导入导出（json + jar + mods 打包） |
| 🕹️ 联机 | 主流启动器式**虚拟局域网联机**：房间码 + MC「对局域网开放」，底层支持陶瓦联机（Terracotta）与 EasyTier |
| 🌐 服务器 | 快捷入口管理（名称/地址/端口/描述） |
| ⚙️ 设置 | 游戏目录、Java 路径、内存、分辨率、下载源、额外 JVM 参数 |

**下载源合规说明**：因法律合规原因，本启动器已**关闭大陆（境内加速镜像）下载功能**，仅允许使用 **Mojang 官方海外源**下载游戏。

---

## 🛠️ 技术栈与架构

```
NovaLauncher/
├── global.json                 # .NET SDK 版本锁定
├── NovaLauncher.sln
└── src/
    ├── NovaLauncher.Core/      # 跨平台核心引擎（无 UI 依赖）
    │   ├── Models/             # 版本清单 / 版本元数据 / 库 / 账号 / 模组 / 服务器 / 整合包模型
    │   ├── Configuration/      # 启动器配置、游戏目录结构、下载镜像
    │   ├── Services/           # 清单、版本、下载、Java、启动、账号、加载器、模组、整合包、服务器
    │   └── Util/               # HTTP、哈希、平台检测
    └── NovaLauncher.App/       # Avalonia UI（MVVM）
        ├── ViewModels/         # 主页 / 版本 / 模组 / 服务器 / 设置
        └── Views/              # 对应 XAML 视图
```

- **.NET 10** + **Avalonia 12**（Fluent 主题）
- **CommunityToolkit.Mvvm**：`[ObservableProperty]` / `[RelayCommand]`
- **Newtonsoft.Json**：解析 Minecraft 复杂 JSON
- 分层清晰，Core 可独立复用与单测

---

## 🔧 编译与发布

### 前置要求
- [.NET SDK 10.0](https://dotnet.microsoft.com/download/dotnet/10.0)

### 编译（调试）
```bash
dotnet build NovaLauncher.sln -c Debug
dotnet run --project src/NovaLauncher.App
```

### 发布（跨平台单文件）
```bash
# Windows x64
dotnet publish src/NovaLauncher.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true

# Linux x64
dotnet publish src/NovaLauncher.App -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true

# macOS (Apple Silicon)
dotnet publish src/NovaLauncher.App -c Release -r osx-arm64 --self-contained true -p:PublishSingleFile=true
```
产物位于 `src/NovaLauncher.App/bin/Release/net10.0/<rid>/publish/`。

---

## 📖 使用说明

首次启动会创建游戏目录 `~/.novalauncher`（Windows 为 `C:\Users\<你>\.novalauncher`）。

### 开始游玩
1. **主页** → 输入昵称 → **离线登录**（或配置 ClientId 后微软登录，或**外置登录**）
2. 选择**游戏版本**（未安装可到「版本管理」下载）
3. 确认 **Java 运行时**（自动检测；不同 MC 版本要求不同的 Java）
4. 设置内存 → **启动游戏**（首次会自动下载补全，可能耗时较长）

### 第三方外置登录（authlib-injector）
用于登录 LittleSkin、Blessing Skin 等第三方 Yggdrasil 认证服务器，可进入开启正版验证的服务器：
1. **主页** → 点击 **外置登录** 展开面板
2. 填入认证服务器 API 地址（如 `https://littleskin.cn/api/yggdrasil`）、账号（邮箱/用户名）和密码
3. 点击 **登录**：启动器连接第三方服务器换取凭证，并自动下载 `authlib-injector.jar`（存于游戏根目录）
4. 之后**启动游戏**时自动注入 `-javaagent:authlib-injector.jar=<服务器地址>` 参数

> 常用皮肤站：LittleSkin `https://littleskin.cn/api/yggdrasil`、Blessing Skin（各站地址不同）、各 MC 服务器自带的外置认证地址。
> `authlib-injector` 自动从 GitHub 官方仓库下载最新版；若网络受限可手动放置 jar 到游戏根目录（`~/.novalauncher/authlib-injector.jar`）。

### 版本管理
- **官方版本下载**：搜索并安装任意版本
- **Fabric**：输入游戏版本 → 加载 Loader 列表 → 安装
- **Forge**：输入游戏版本 + Forge 版本号 → 下载并运行安装器
- **整合包**：导入 `.zip` / 导出当前版本为 NovaPack

### 模组管理
选择版本后列出 `mods/` 下文件，可启用/禁用/删除/打开文件夹。
- **检测冲突**：点击「检测冲突」自动扫描该版本 `mods/` 下的 jar，读取各模组清单（Fabric `fabric.mod.json` / Forge `mods.toml` / 旧版 `mcmod.info`）识别同一模组的多个版本，并在页面底部黄色区列出冲突项（同一模组存在多个 jar 可能导致启动失败，需保留一个版本、删除其余）。
- **检查依赖**：点击「检查依赖」读取各模组清单中的必装依赖声明，与已装模组比对，列出缺失的前置模组（自动排除 Minecraft/加载器/Java 等环境依赖）。缺失的前置可用「模组下载」搜索安装。

### Java 内置下载
- **主页** → Java 运行时旁点击 **下载所需Java**：自动从 Adoptium 下载当前所选版本所需的 JDK 并内置
- **启动时自动补齐**：若系统 Java 版本不满足所选版本（如 1.18+ 需 Java 17/21），会自动下载对应 JDK 后启动
- 内置 JDK 存放于 `~/.novalauncher/java/{主版本}/`

### 光影 / 资源包下载
在「模组下载」页把**类型**切换为「光影」或「资源包」，即可搜索并下载到对应版本的 `shaderpacks` / `resourcepacks` 目录（如 Complementary 光影）。

### 联机（虚拟局域网）
在左侧导航进入「联机」（主流启动器做法）：
1. **一键安装组件**：点击「一键下载并安装 EasyTier 联机组件」，自动从 GitHub 下载对应平台的 EasyTier（陶瓦联机底层，约 25MB）到游戏目录 `tools/`（已内置自动下载，无需手动放置）
2. **房主**：点「创建房间」生成房间码 → **复制房间码**发给好友 → 点「开始」进入虚拟局域网 → 进游戏后按 Esc 点「对局域网开放」
3. **好友**：输入房间码 → 点「加入房间并开始」进入同一虚拟局域网 → 在游戏「多人游戏」用 **虚拟 IP:端口** 连接房主的局域网世界
4. 结束后点「停止联机」
5. **版本维护**：可「检查更新」EasyTier 最新版本，发现新版本时点「升级 EasyTier」自动更新

> 说明：陶瓦联机本体是 HMCL 的 Java 内置组件，无法直接内嵌到本 C# 启动器，故以其底层开源的 **EasyTier**（启动器内置自动下载）及可独立运行的陶瓦联机作为后端。与 HMCL/PCL/FMCL（同样支持陶瓦联机）可互通。真实联机效果需两台联网设备验证。

### 模组下载
在左侧导航进入「模组下载」：
1. 选择**数据源**（Modrinth / CurseForge）、**类型**（模组/光影/资源包/整合包）、**游戏版本**、**加载器**（fabric/forge/neoforge/不限）
2. 输入关键词**搜索** → 点击模组查看可用版本
3. 选择**目标版本**（下载到的本地游戏版本），点版本旁的**下载** → jar 自动存入该版本 `mods/`（光影进 `shaderpacks/`、资源包进 `resourcepacks/`）
4. **批量下载**：搜索结果顶部「批量下载全部到目标版本」，一键把当前搜索结果每个项目（取第一个匹配版本）自动下载到目标版本，实时显示进度与成功/跳过/失败统计；已下载的模组会在结果列表中打 **✓ 已下载** 标记
5. **整合包一键导入**：类型选「整合包」→ 搜索 Modrinth 整合包 → 选版本点「下载」，自动下载 `.mrpack` 并解压解析，把其中全部模组下载到该版本 `mods/`、`overrides/` 配置合并进版本目录（已存在的模组自动跳过），导入时实时显示「正在导入模组 第 i/n 个：文件名」进度明细
6. 「打开目标 mods 文件夹」可直接查看已下载模组

> **关于 MC百科**：MC百科（mcmod.cn）没有公开 API、不托管模组文件（下载均跳转到 CurseForge/Modrinth）且网页有反爬，无法直接抓取其全部模组。本启动器通过 **Modrinth + CurseForge** 官方 API 覆盖 MC百科收录的绝大多数主流模组，可真实搜索并下载 jar。若需 CurseForge 源，先在「设置」页填入申请到的 API Key。

### Java 版本与 MC 版本对照
| MC 版本 | 建议 Java |
|---------|-----------|
| 1.12.2 及更早 | Java 8 |
| 1.13 – 1.16 | Java 8 / 11 |
| 1.17 – 1.17.1 | Java 16 / 17 |
| 1.18 – 1.20.4 | Java 17 |
| 1.20.5+ | Java 21 |

启动器会自动校验当前 Java 主版本是否满足版本要求。

---

## ⚠️ 已知限制
- **微软正版登录**：需要自行在 Azure 门户注册桌面应用，将 `ClientId` 填入「设置」页后启用（Device Code 流程）。
- **第三方外置登录**：依赖目标认证服务器兼容 authlib-injector；凭证（access token）有效期较短，失效后需在主页重新外置登录。
- **Forge**：通过官方 Installer 安装，需本机具备对应 Java 版本，安装过程较慢。
- **联机组件**：需本机安装陶瓦联机或 EasyTier 组网组件；真实联机效果需两台设备验证（云端单机无法端到端测试）。
- **下载源合规**：因法律合规原因，已关闭大陆（境内加速镜像）下载功能，仅允许官方海外源下载；在设置页选择「大陆源」会显示提示并强制回退。
- **快速加入服务器**（`--quickPlayMultiplayer`）仅对支持该参数的新版本（约 1.20.2+）有效。
- 服务器快捷入口目前提供条目管理与地址记录，加入服务器需在游戏内输入地址；新版本可在启动时自动携带地址。

---

## ✅ 验证情况
- 解决方案编译 **0 警告 / 0 错误**（.NET 10 + Avalonia 12）
- 应用在 Linux 虚拟显示下启动，UI 完整渲染（侧边导航、主页、版本、模组、服务器、设置五页）
- 核心引擎冒烟测试通过：官方版本清单获取（920 个版本）、`1.20.1` 版本 JSON 解析（88 库 / Java 17）、库收集与启动参数组装
- 系统 Java 自动检测正常

> 完整游戏启动需在具备对应 Java 版本的真实图形环境中进行，沙箱环境未做端到端游戏启动测试。
