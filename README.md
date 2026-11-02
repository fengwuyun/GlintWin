<div align="center">

<img src="app.ico" width="76" alt="GlintWin" />

# GlintWin

**打字时，顺手学英文**

用搜狗或微软拼音打中文，候选栏下方实时浮出第一候选词的英文释义。
不换输入法、不改输入习惯、不联网。

<sub>Windows 10 2004+ / 11 · x64 · .NET 10 自包含单文件 · 内置 12 万条词典 · 全程离线</sub>

</div>

<img src="assets/glintwin-overview.jpg" width="620" alt="GlintWin 概览：悬浮释义、辅助记忆、轻量不打断" />

---

## 这是什么

输入 `jin'zhan` 时，候选栏下方跟着浮出一条小胶囊：`进展 — make headway · make progress`。
它不接管输入、不弹窗打断，只是在你看候选词的那一眼，顺手给一个英文说法。

和 macOS 上的 [Glint](https://github.com/jsogn/Glint) 同一套产品思路，Windows 侧独立实现：
候选词由**系统 OCR 从输入法候选窗读取**，释义查**本地 CC-CEDICT 词典**，中间不经过网络。

## 功能

| 能力 | 说明 |
|---|---|
| **悬浮释义** | 候选窗下方浮层显示第一候选的英文；取词典前两条释义、去掉 `to ` 前缀、以 ` · ` 连接 |
| **双输入法通道** | 搜狗走候选窗口锚定（全应用可用）；微软拼音走「键序追踪 + 光标锚定 + 光标下方条带 OCR」，搜狗可见时自动让位 |
| **12 万条离线词典** | CC-CEDICT 基础库 + 5 个主题词包：互联网热词 68 / AI 术语 63 / 短剧黑话 63 / 日常表达 60 / AI·编程·办公 95 |
| **自定义词库** | 界面表格增删改、CSV 导入导出；自定义词条优先级最高，保存即生效 |
| **跟随皮肤配色** | 悬浮条底色/文字/字号采样自候选行像素；也可 9 组预设一键换肤，或用 HSV 取色器自定义 |
| **透明与外观** | 不透明度 40–100 可调，实时棋盘格预览；字号、垂直偏移可配 |
| **每日词汇（可选）** | 默认关闭。开启后本地记录当天输入过的词，按日期/状态筛选、逐条核对，确认后一键加入自定义词库 |
| **轻量不打断** | 悬浮条 `NOACTIVATE` + 鼠标穿透，绝不抢焦点；40 ms 轮询、像素哈希去重、首帧双确认与指数平滑防闪烁 |
| **全离线** | 无网络代码、不注入进程、不读正文、不落盘原始按键 |

## 三步上手

| 步骤 | 做什么 |
|---|---|
| **01 打开工具** | 下载单文件 EXE，双击运行。托盘出现「译」图标即就绪。 |
| **02 正常打字** | 照常用搜狗 / 微软拼音输中文，不用切换任何模式。 |
| **03 看英文释义** | 候选栏下方浮出胶囊，显示当前第一候选的英文。 |

## 下载与运行

从 [Releases](https://github.com/fengwuyun/GlintWin/releases/latest) 下载 `GlintWin-v*-win-x64.exe`（约 183 MB，内含 .NET 10 运行时、基础词典与词包），双击即用，**免安装**。

| 项 | 要求 |
|---|---|
| 系统 | Windows 10 2004（19041）/ Windows 11，x64 |
| 输入法 | 搜狗输入法（完整支持）；微软拼音（已支持，见限制） |
| 系统组件 | **Windows 中文 OCR 语言包**（中文系统默认自带；缺失时状态总览会提示 OCR 未就绪） |
| 权限 | 普通用户即可；管理员权限窗口无法截图，故不支持 |

- 首次运行把内置资源释放到 `%LOCALAPPDATA%\GlintWin\`；升级只补缺不覆盖用户数据。
- 开机自启：`Win+R` → `shell:startup` → 放入 `GlintWin.exe` 的快捷方式（也可在主界面「通用设置」里一键开）。
- 托盘右键：**打开主界面 / 暂停 / 退出**。暂停同时暂停每日采集。

## 主界面

深色 Fluent 风格控制台，首次运行自动打开，之后从托盘「打开主界面」进入，左侧五页：

| 页 | 内容 |
|---|---|
| 状态总览 | 双通道监控状态、OCR 是否就绪、词典构成（基础 / 词包 / 自定义）、已启用词包 |
| 悬浮条外观 | 取色模式（自动跟随皮肤 / 自定义）、9 组预设、HSV 取色器、不透明度滑杆、实时透底预览 |
| 词库管理 | 主题词包开关（即时重载）、自动发现 `Data/Packs/*.json` 新词包、自定义词库表格与 CSV |
| 每日词汇 | 自动采集开关、按日期/状态/文本筛选、逐条核对保存、忽略或加入自定义词库 |
| 通用设置 | 开机自启、诊断入口、关于 |

配置即时生效并写入 `%LOCALAPPDATA%\GlintWin\config.json`：

```json
{
  "PollIntervalMs": 40,
  "OffsetYPx": 4,
  "FontSizeDip": 15,
  "CandidateWindowClass": "SoPY_Comp",
  "OcrLanguage": "zh-Hans-CN",
  "DictionaryPath": "Data/cedict.json",
  "PillMode": "auto",
  "PillBgColor": "#FF2A2C30",
  "PillFgColor": "#FFF2F5FA",
  "PillOpacityPercent": 85,
  "EnabledPacks": ["internet", "ai", "shortdrama", "modern_daily", "modern_tech"]
}
```

| 键 | 默认 | 说明 |
|---|---|---|
| `PillMode` | `auto` | `auto` 跟随输入法皮肤取色；`custom` 用 `PillBgColor` / `PillFgColor` |
| `PillOpacityPercent` | `85` | 40–100；皮肤本身透底，建议 80–90 |
| `OffsetYPx` / `FontSizeDip` | `4` / `15` | 悬浮条相对候选窗的垂直偏移与字号 |
| `EnabledPacks` | 全部 5 包 | 启用哪些主题词包，对应 `Data/Packs/*.json` |
| `AutoCollectDailyInput` | `false` | 每日词汇自动采集；`CollectionExcludedApps` 填进程名排除应用 |
| `Msp*`（高级） | — | 微软拼音通道的采样间隔（120 ms）与 OCR 条带尺寸（560×120） |

数据与日志：

```
%LOCALAPPDATA%\GlintWin\
├─ config.json          # 配置
├─ Data\cedict.json     # 基础词典（CC-CEDICT 转换）
├─ Data\Packs\*.json    # 主题词包
├─ Data\custom.json     # 自定义词库（配 custom_template.csv 模板）
├─ daily-words.db       # 每日词汇（SQLite，仅开启采集后写入）
└─ logs\glint-YYYYMMDD.log   # 日志，超 5 MB 轮转 .old
```

### 扩词库的三种方式

1. **界面改**：词库管理页表格增删改，保存即重载。
2. **CSV 导**：列 `词条,拼音,释义1[,释义2,释义3]`，UTF-8 带 BOM，模板 `Data/custom_template.csv`。
3. **做词包**：任意 `{"词":{"p":"拼音","s":["释义"]}}` 格式的 JSON 丢进 `Data/Packs/`，重启后自动出现在词库页。

## 支持范围与已知限制

- **搜狗输入法**：完整支持（窗口锚定，全应用可用）。
- **微软拼音**：需要应用暴露 Win32 光标才可用——记事本 / WinForms / 多数原生应用可以，Chrome、VS Code 等自绘光标的应用暂不支持。
- 释义只取**第一候选**（高亮项），跟随翻页选中项在路线图上。
- 拼音纠错在搜狗通道依赖拼音框可读性（艺术字皮肤下保守不触发）；微软拼音通道用键盘拼音，更稳。
- 个别低对比度艺术字皮肤取色会回退深色主题。
- 管理员权限窗口受会话隔离限制，无法截图，暂不支持。

## 工作原理

```
搜狗通道：轮询候选窗 SoPY_Comp(40ms) → 窗口小区域截图 → 像素哈希去重
微软拼音通道：键盘拼音追踪(组词信号) + 光标坐标锚定 + 光标下方条带 OCR
        ↓（搜狗候选窗可见时，微软通道让位）
公共管线：2x 放大 OCR(zh-Hans-CN) → 按词级坐标间隙切分候选块
        → 候选行像素采样出皮肤配色
        → CC-CEDICT 查询（整词 → 最长前缀切分 ≤6 字 → 拼音交叉验证纠错）
        → 胶囊悬浮条定位到输入窗正下方
```

- 外观：背景/文字色/字号直接采样候选行像素，因此能跟着任意皮肤走。
- 释义：CC-CEDICT 前两义，去 `to ` 前缀，` · ` 连接；OCR 疑似错字时用拼音交叉验证纠正，宁缺勿错。
- 防闪：主题色/字号指数平滑，变化低于阈值不重绘；解析偶发失败保持原样 500 ms；首帧需连续两帧一致。
- 定位（v0.4.2）：初始定位需两次间隔 60–400 ms 的相符采样，输入轮次变化会作废旧定位与过期 OCR 结果。

## 隐私边界

架构级约束，不是可选项：

- **全离线**：无网络代码（可审计）；OCR 用系统引擎，词典在本地。
- **不读正文**：只截取输入法候选窗 / 光标下方的小条带，不碰文档内容。
- **键盘钩子只认拼音字母**：仅记录 A–Z 序列（退格修正，空格/回车/ESC 与 Ctrl/Alt 组合立即清空），用于判断「正在组词」和拼音纠错；不记录其他按键、不持久化原始按键。
- **不注入进程**，不写剪贴板，不上传任何内容。
- 只有主动开启每日采集后，才会把「待核对词条」存进本地 `daily-words.db`。

## 从源码构建

需要 .NET 10 SDK。

```bash
dotnet build -c Release

# 回归检查（各自独立测试数据）
dotnet run --project tools/ParserTest    # 候选解析（含真实 OCR 乱码用例）
dotnet run --project tools/PositionTest  # 位置状态机
dotnet run --project tools/DailyTest     # 每日词汇
dotnet run --project tools/ConfigTest
dotnet run --project tools/UiTest -- <截图输出目录>

# 单文件发布（自带运行时，含词典与词包）
dotnet publish -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
```

发布后验证与端到端：

```bash
GlintWin.exe --verify-package <独立检查目录>   # 生成 package-check.json：资源释放、词条数、OCR 可用性
tools/e2e_typetest.ps1                        # 自动打字 + 截图验证（先结束已运行的 GlintWin）
```

重建基础词典（CC-CEDICT → `Data/cedict.json`，约 12 万条）：

```bash
python tools/convert_cedict.py
```

## 参考与致谢

- **[jsogn/Glint](https://github.com/jsogn/Glint)** — 本项目的灵感与产品原型来源。原版是 macOS 原生应用（Swift / AppKit），直接读输入法候选；
  Windows 侧无法复用其平台层，因此 GlintWin 是**独立实现**：候选词改由系统 OCR 从候选窗像素读取，释义改为查本地词典。交互形态与「顺手学英文」的产品判断沿用自上游。
- **[CC-CEDICT](https://cc-cedict.org/)** — 基础词典数据来源（CC-BY-SA 4.0）。
- **Windows.Media.Ocr** — 系统 OCR 引擎，候选词识别全部在本地完成。
- **[.NET 单文件发布](https://learn.microsoft.com/dotnet/core/deploying/single-file/)** — 免安装分发的实现方式。

## 版本记录

| 版本 | 要点 |
|---|---|
| [v0.4.2](docs/RELEASE-0.4.2.md) | 稳定初始定位（双帧确认 + 输入轮次作废旧坐标）；新增日常表达 / AI·编程·办公两个词包；词库页自动发现新词包 |
| [v0.4.1](docs/RELEASE-0.4.1.md) | 词典常驻内存 35.50 → 23.72 MiB（-33%）；精确查词零重复分配；每日采集关闭时数据库按需初始化 |
| [v0.4](docs/RELEASE-0.4.md) | 悬浮条位置去抖；可选每日词汇收集箱；单文件 EXE 内置运行时与词典；数据迁至 `%LOCALAPPDATA%` |

更多文档：[PRD](docs/PRD.md) · [技术说明](docs/TECHNICAL.md) · [开发计划](docs/PLAN.md) · [验收清单](docs/ACCEPTANCE.md)
