# cTools

Windows 优先的极速应用启动器。当前版本聚焦一件事：**把应用与自定义文件的启动做到足够快、范围足够可控、界面足够安静**。

插件平台保留为后续阶段；这一版先把启动器本身打磨完整，包括搜索范围、文件类型、自定义启动项、全局快捷键、呼出屏幕与位置、列表 / 网格布局、Esc 与空格键行为，以及全屏保护。

参考产品：

- [uTools](https://www.u-tools.cn/)：默认搜索范围、主面板交互、全局快捷键。
- [PowerToys Command Palette](https://learn.microsoft.com/en-us/windows/powertoys/command-palette/overview)：紧凑模式、监控器位置、Esc 行为、搜索文本保留、全屏保护、固定项。

---

## 下载 / 安装

每次 `master` 分支更新都会由 GitHub Actions 自动构建并发布到 [Releases](https://github.com/lx1736767412/cTools/releases)：

- **安装版**：`cTools-<version>-setup.exe`，按当前用户安装（`%LOCALAPPDATA%\Programs\cTools`），不需要管理员权限。
- **便携版**：`cTools-<version>-win-x64.zip`，解压后直接运行 `cTools.exe`。

安装包未做代码签名，SmartScreen 提示“未知发布者”时选择“更多信息 → 仍要运行”。

### 自动更新

安装版会在启动约 20 秒后检查最新 Release，此后每 6 小时检查一次；也可以从「设置 → 关于」或托盘菜单手动检查。发现新版本后可一键下载并静默安装，安装完成后 cTools 会自动重新打开。便携版不执行自更新，只提供发布页下载入口。

更新包下载后会按 GitHub 提供的 SHA-256 摘要校验，校验失败会放弃安装。

本地打包需要 Inno Setup 6，可执行：

```powershell
dotnet publish src\cTools.App -c Release -r win-x64 --self-contained true -o publish
ISCC.exe /DMyAppVersion=0.1.0 /DPublishDir="$PWD\publish" /DOutputDir="$PWD\dist" installer\cTools.iss
```

---

## 快速开始

环境：Windows 10/11 + .NET 9 SDK。

```powershell
cd D:\bored\cTools
dotnet build cTools.sln -c Debug
dotnet run --project src\cTools.App
```

> 项目、程序集和解决方案名均为 cTools，仓库目录任意。

带查询或设置页启动（开发 / 截图用）：

```powershell
dotnet run --project src\cTools.App -- --query chrome
dotnet run --project src\cTools.App -- --settings --tab 2
```

其他参数：

| 参数 | 作用 |
|---|---|
| `--background` | 只启动托盘与快捷键，不显示窗口（开机启动使用） |
| `--query <text>` | 启动后填入搜索词 |
| `--settings` | 启动后打开设置 |
| `--tab <0-5>` | 设置页初始标签：常规 / 外观与布局 / 搜索范围 / 自定义启动 / 快捷键与行为 / 关于 |
| `--updated` | 由安装包在自动更新完成后传入，普通启动可忽略 |

---

## 功能

### 启动器

- 无边框、置顶、圆角、深浅色主题；显示在指定显示器的指定竖直位置。
- 输入即搜；`↑` `↓` 选择，`←` `→`（网格模式）移动，`Enter` 打开，`Ctrl+Enter` 管理员，`Shift+Enter` 打开所在位置，`Esc` 关闭 / 清空。
- 列表与网格两种结果布局，切换即时生效；网格标准宽度下 8 列，只保留图标与名称，名称最多两行居中显示，再长才省略（悬停显示完整名称）。
- 结果支持图标、固定、最近使用 / 使用次数加权、管理员标记。
- 失焦自动隐藏；可设置「打开时选中文本」「重新打开保留搜索内容」。
- 紧凑模式：没有输入时只显示搜索框，输入后展开结果。
- 把文件 / 文件夹拖入主窗口即添加为自定义启动项；空结果页也可以直接「添加文件…」。
- 呼出时使用轻量的淡入 + 上移动画，并尊重系统「减少动画」设置。

### 交互行为（可在设置中调整）

| 设置 | 说明 |
|---|---|
| 全局快捷键 | 点击输入框后直接按组合键；支持 `Alt+Space`、`Ctrl+Shift+1` 等；无法注册时自动回退低级键盘钩子。 |
| 呼出屏幕 | 鼠标所在屏幕 / 主显示器 / 当前窗口所在屏幕 / 固定某一块显示器。 |
| 呼出位置 | 顶部 / 上部 / 居中 / 下部 / 底部；在可用工作区内水平居中。 |
| 全屏保护 | 检测到游戏、视频或演示模式全屏时忽略快捷键，不打断全屏应用。 |
| 空格确认 | 开启后空格等同 Enter；此时不能用空格做分词搜索（设置中已注明）。 |
| Esc 行为 | 先清空再隐藏，或始终直接隐藏。 |
| 结果布局 | 列表 / 网格。 |
| 失焦隐藏、搜索文本保留、紧凑模式、结果数量 | 常规页可调。 |

### 搜索范围（默认参考 uTools）

默认开启：

- 已安装应用（`shell:AppsFolder`，同时覆盖 Win32、带 AUMID 的应用和 UWP / MSIX）
- 开始菜单 / 桌面快捷方式（用于补充 AppsFolder 没有收录的项）
- 注册表 `App Paths`（只补充没有重复的项）
- 自定义启动项 / 自定义扫描路径

默认关闭：

- Windows 设置页面（可选）
- 任意磁盘全文搜索（本版不做）

扫描范围控制：

- **最大扫描深度**：开始菜单、桌面与自定义路径共用（1–12 层）。
- **文件类型**：默认只收录 `.exe` 与 `.lnk`；`.url`、ClickOnce `.appref-ms`、`.bat/.cmd`、`.ps1`、`.msi` 默认关闭，可在设置里重新打开，也可以追加任意扩展名（例如 `.ahk`、`.py`、`.jar`）。
- **自定义扫描路径**：位于「设置 → 自定义启动」顶部，可以添加整个文件夹，也可以只添加一个文件；每一条都可以单独启用 / 停用，文件夹还可以选择是否递归。
- **排除关键词**：每行一个，支持 `*` 通配；只影响系统来源，不影响用户显式添加的自定义启动项。
- 单个显式添加的文件不受扩展名白名单限制，适合把 `.md`、`.docx`、`.psd` 等文档直接放进索引。
- **去重**：同名、同目标的可执行文件只保留一条；App Paths 与开始菜单 / 桌面指向同一程序（例如 DingTalk / 钉钉、Doubao / 豆包）时会自动合并。
- 「重建索引」会按当前设置重新扫描并显示各来源数量；所有设置修改都会立即生效并自动保存。

过滤规则：

- 系统来源跳过 `uninstall`、`卸载`、`readme`、`help`、`website`、`文档`、`manual`、`license`、`documentation` 等名称。
- 系统快捷方式目录跳过 SDK/docs/samples/demos 等目录。
- `.url` 中的 http/https 网页快捷方式不进入开始菜单索引；自定义扫描路径中的 `.url` 会解析为网址。
- `shell:AppsFolder` 会跳过帮助文档、网页和 Shell 基础设施包。

### 自定义启动

在「设置 → 自定义启动」里添加：

- 任意文件（不限于可执行文件，使用系统默认程序打开）
- 文件夹
- URL
- 命令（通过 `cmd /c` 执行）
- 别名、关键词、参数、工作目录、自定义图标
- 管理员运行、启用 / 停用
- 列表内直接拖入文件或文件夹

---

## 设置项总览

| 标签 | 包含 |
|---|---|
| 常规 | 开机启动、托盘图标、失焦隐藏、紧凑模式、搜索文本选择 / 保留、结果数量 |
| 外观与布局 | 主题、呼出屏幕、呼出位置、列表 / 网格 |
| 搜索范围 | 来源开关、递归、最大深度、扫描文件类型、自定义路径（文件 / 文件夹 / 递归 / 启用）、排除关键词、预览重建与来源计数 |
| 自定义启动 | 文件 / 文件夹 / 网址 / 命令、编辑、删除、启用、管理员运行、拖放添加 |
| 快捷键与行为 | 全局快捷键、全屏保护、空格确认、Esc 行为 |
| 关于 | 版本、数据目录、日志入口 |

---

## 界面设计

这一版把原来的「设置项平铺 + 默认 WPF 控件」重做为一套更接近 Windows 11 工具的设计语言：

- 左侧分组导航 + 右侧卡片式设置，替代横向 Tab 和长表单。
- 统一圆角、细边框、留白、分段选择器、开关式复选、Kbd 提示胶囊。
- 主面板采用搜索头 + 结果区 + 快捷键提示底栏；列表与网格共享同一套选中态。
- 深色主题下修正了旧版 ComboBox / 表头写死浅色的问题；列表、表头、滚动条都有对应的深色表现。
- 所有设置修改即时生效并自动保存，设置页保持打开，方便继续调整。

仍然刻意保持克制：没有复杂的页面转场、没有为了动效牺牲首帧速度；窗口材质依赖系统 DWM，在部分远程桌面 / 精简系统上会退化为普通背景。

---

## 技术决策：为什么是 C# + WPF，而不是 Rust/C

结论：**这一版用 C#/.NET 9 + WPF + Win32 P/Invoke**，并把核心搜索/索引设计成可替换的边界；如果后续 profile 证明索引是瓶颈，再把 `cTools.Core.Index` 换成 Rust DLL，不动 UI。

理由：

1. **瓶颈不在语言**。启动器的热路径是窗口显示 + 搜索 + 列表渲染。uTools 本身是 Electron 也能做到很快；慢更多是搜索链路 / 窗口实现问题，不是 JS 本身。
2. **C# 足够快**。本机 180 个应用，查询稳定在亚毫秒级；1 万条模拟数据也保持在毫秒级。
3. **WPF 的中文输入法、无障碍、列表虚拟化、窗口材质是现成的**。Rust/C 写原生 UI 要把 IME、高 DPI、无障碍、字体、弹窗全部重做，边际收益只有几毫秒。
4. **PowerToys Command Palette 本身就是 C#/WinUI 3**，证明托管 UI 可以做到足够快。
5. **本机工具链就绪**：.NET 9 SDK 已安装；Rust/Visual Studio C++ 工具链未安装，引入它们会先花 2–5GB 和数小时。

如果未来要做 macOS/Linux，优先把 `cTools.Core`（扫描/索引/启动抽象）保持平台无关，UI 再按平台替换；而不是现在为了「可能的跨平台」牺牲 Windows 体验。

---

## 性能

本机实测（Windows 11，180 个应用条目）：

| 指标 | 结果 |
|---|---|
| 冷扫描（开始菜单 + 桌面 + App Paths + UWP） | ~0.5s |
| 索引构建（180 项） | ~11–16ms |
| 单次查询（180 项，`wx` / `微信` / `chrome` / `vsc`） | 0.02–0.06ms |
| 索引构建（10000 条模拟数据） | ~35ms |
| 单次查询（10000 条，平均 / P95） | ~0.54ms / ~0.60ms |
| 索引缓存加载 | 启动时同步读取，约几十毫秒 |
| 全局快捷键 | `RegisterHotKey` 失败时自动回退低级键盘钩子；`Alt+Space` 已实测可用 |

性能设计：

- 索引在后台 STA 线程扫描；UI 先读缓存，秒开。
- 搜索为纯内存数组扫描，名称/拼音/首字母/英文缩写全部预计算。
- 输入 20ms 合并；结果列表虚拟化；图标异步加载并缓存。
- 窗口启动时 `EnsureHandle()` 预热，呼出只是 `Show + Activate`。
- 索引缓存原子写入 `%LOCALAPPDATA%\cTools\index.json`。

---

## 项目结构

```
cTools/
├─ src/
│  ├─ cTools.App/            WPF UI、窗口、托盘、快捷键、设置
│  │  ├─ Services/           Hotkey / Monitor / Fullscreen / Tray / Theme / WindowEffects / Icon / SingleInstance
│  │  ├─ ViewModels/         MainViewModel / ResultItem
│  │  ├─ MainWindow.xaml     主搜索面板（列表 / 网格）
│  │  ├─ SettingsWindow.xaml 设置（左侧导航）
│  │  └─ CustomEntryWindow.xaml 自定义启动项编辑
│  ├─ cTools.Core/           平台核心（可替换为 Rust）
│  │  ├─ Models/             AppEntry / AppSettings / EntryKindDetector
│  │  ├─ Index/              归一化 / 拼音 / 搜索评分
│  │  ├─ Scanning/           AppsFolder / 开始菜单 / 桌面 / App Paths / 自定义路径
│  │  ├─ Launching/          ShellExecuteEx / UWP / 管理员
│  │  ├─ Storage/            设置与索引缓存
│  │  └─ Data/pinyin.json    20775 个常用汉字的全拼/首字母
│  └─ cTools.Cli/            扫描/搜索/基准/启动测试 CLI
├─ tools/pinyin-gen/         生成 pinyin.json 的脚本（开发用）
├─ tools/ui-preview/         无桌面环境下的离屏 UI 渲染（开发用）
└─ cTools.sln
```

数据目录：

| 内容 | 路径 |
|---|---|
| 设置 | `%APPDATA%\cTools\settings.json` |
| 索引缓存 | `%LOCALAPPDATA%\cTools\index.json` |
| 日志 | `%LOCALAPPDATA%\cTools\logs\ctools.log` |

---

## CLI（开发/验证）

```powershell
# 扫描并写入索引缓存
dotnet run --project src\cTools.Cli -- scan

# 搜索
dotnet run --project src\cTools.Cli -- search wx
dotnet run --project src\cTools.Cli -- search 微信

# 性能基准
dotnet run --project src\cTools.Cli -- bench

# 查看默认扫描来源
dotnet run --project src\cTools.Cli -- sources

# 测试启动
dotnet run --project src\cTools.Cli -- launch notepad.exe
```

无交互桌面 / CI 环境可以用离屏渲染检查界面：

```powershell
dotnet run --project tools\ui-preview -- artifacts\ui
```

会输出主面板（列表 / 网格 / 空状态 / 紧凑）、设置各页、自定义编辑器的 PNG。

---

## 已知限制 / 下一步

当前限制：

- 只支持 Windows。
- 不做任意磁盘文件内容搜索（只做应用 / 快捷方式 / 自定义路径）。
- UWP 图标已通过 `shell:AppsFolder` + `IShellItemImageFactory` 提取；极少数包可能仍回退通用图标。
- 全屏检测是「通知状态 + 前台窗口覆盖显示器」的启发式；少数组件覆盖层可能误判。
- 固定显示器被拔掉时会回退到鼠标所在屏幕，不记忆“上次位置”。
- 自动更新只对安装版生效；便携版需要手动下载新的压缩包。
- 安装包和更新包都没有代码签名，首次运行可能触发 SmartScreen 提示。
- 没有插件系统（按当前阶段聚焦启动器）。

下一步优先级：

1. 代码签名与增量更新（减少下载体积）。
2. 扫描范围「每来源明细」：展开显示每个来源扫到多少项、哪些被排除。
3. 索引增量监听（开始菜单 / 桌面 / 自定义路径变化后 500ms 内更新）。
4. 文件 / 文件夹搜索（可选，使用 NTFS USN Journal 或 Everything SDK，而不是全盘遍历）。
5. 性能回归基准（BenchmarkDotNet / 启动到可见 p95）。
6. 插件平台：在当前启动器交互稳定后，再接入插件清单、进程隔离与动作面板。

---

## 许可证

本项目以 [WTFPL](LICENSE)（Do What The Fuck You Want To Public License, Version 2）开源：你可以随意复制、修改、分发，甚至改名后据为己有。

uTools / PowerToys / ZeroLaunch-rs 仅作为产品与交互参考，项目未复制其源码。
拼音数据由 [pinyin-pro](https://github.com/zh-lx/pinyin-pro)（MIT）生成，见 `tools/pinyin-gen/`。

