# GlintWin — 搜狗候选词英文释义悬浮条

对标 macOS 应用 Glint 的 Windows 版 MVP：**不替换输入法**，用搜狗打字时，
在候选栏下方实时显示第一候选词的英文释义（如「学习 → learn · study」）。
全离线：系统自带 OCR + 本地 CC-CEDICT 词典，不联网、不注入、不读正文。

## 运行

- 双击 `publish\GlintWin.exe`（单文件，自带 .NET 10 运行时，无需安装）。
- 托盘右键菜单：**暂停 / 退出**。
- 开机自启：`Win+R` 输入 `shell:startup`，把 `GlintWin.exe` 的快捷方式放进去。

## 工作原理

```
双通道输入：
① 搜狗通道：轮询候选窗口(SoPY_Comp, 40ms) → 窗口区域截图 → 像素哈希去重
② 微软拼音通道：Win11 候选窗为 DirectComposition 渲染（无窗口/无 UIA 可读），
   采用「键盘拼音追踪(组词信号) + 光标坐标锚定 + 光标下方条带 OCR」，
   搜狗候选窗可见时自动让位
公共管线：2x 放大 OCR(zh-Hans-CN) → 词级坐标间隙切分候选块 → 候选行/拼音框像素采样皮肤配色
→ CC-CEDICT 查询（整词 → 前缀切分 → 拼音交叉验证纠错）→ 胶囊悬浮条恒在输入窗正下方
```

- 外观自适应输入法皮肤：悬浮条背景/文字色/字号直接采样自当前皮肤的候选行。
- 悬浮条绝不抢焦点（NOACTIVATE）且鼠标穿透，不影响打字。
- 防闪烁：主题色/字号指数平滑、变化低于阈值不重绘、解析偶发失败 500ms 保持原样、
  首显需连续两帧一致。
- 释义格式：取 CC-CEDICT 前两个释义，去掉 "to " 前缀，用 ` · ` 连接；
  整词查不到时按最长前缀切分（≤6 字）；OCR 疑似错字时用组词拼音交叉验证纠正（宁缺勿错）。

## 隐私边界

- 全离线：无网络代码（可审计）、OCR 用系统引擎、词典本地。
- **不读取屏幕正文**：只截取输入法候选窗/光标下方小条带。
- **键盘钩子仅识别拼音字母**：低级钩子只记录 A-Z 序列（退格可修正，空格/回车/ESC 立即清空，
  Ctrl/Alt 组合清空），用于判断「正在组词」与拼音纠错；不记录其他按键、不持久化、不上传。
- 无进程注入；源码可审计。

## 主界面与配置

深色 Fluent 风格控制台，左侧导航四页（**托盘右键 → 「打开主界面」**；首次运行自动打开）：

- **状态总览**：双通道监控状态、词典构成（基础/词包/自定义）、启用的词包
- **悬浮条外观**：取色模式（自动跟随皮肤 / 自定义）、**9 组预设调色盘**一键换肤、
  **HSV 可视化取色器**（色相条 + 饱和度/亮度面板，拖动即改）、不透明度滑杆、
  棋盘格**实时预览**透底效果
- **词库管理**：主题词包开关（**互联网热词 68 词 / AI 术语 63 词 / 短剧黑话 63 词**，启停即时重载）、
  **自定义词库管理**（表格增删改、CSV 导入导出、模板文件 `Data\custom_template.csv`）
- **通用设置**：开机自启开关、诊断入口、关于

修改即时生效；「保存配置」写入 exe 同目录 `config.json`（也可手改）：

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
  "EnabledPacks": [ "internet", "ai", "shortdrama" ]
}
```

- `PillMode`：`auto`（跟随皮肤取色，默认）/ `custom`（使用 PillBgColor/PillFgColor）
- `PillOpacityPercent`：40–100，透明度（皮肤候选栏本身透底，建议 80-90）
- `EnabledPacks`：启用的主题词包，对应 `Data/Packs/*.json`

### 自定义词库

- 界面：词库管理页表格直接增删改，保存后即时重载（自定义词条**优先于**主词典与词包）。
- CSV：列 `词条,拼音,释义1[,释义2,释义3]`，UTF-8（带 BOM）；模板见 `Data\custom_template.csv`。
- 也允许把任意词包 JSON（`{"词":{"p":"拼音","s":["释义"]}}`）丢进 `Data\Packs\` 当新词包。

日志：`%LOCALAPPDATA%\GlintWin\logs\glint-YYYYMMDD.log`（超 5MB 自动轮转 .old）

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
  "PillOpacityPercent": 85
}
```

- `PillMode`：`auto`（跟随皮肤取色，默认）/ `custom`（使用 PillBgColor/PillFgColor）
- `PillOpacityPercent`：40–100，透明度（皮肤候选栏本身透底，建议 80-90）

日志：`%LOCALAPPDATA%\GlintWin\logs\glint-YYYYMMDD.log`（超 5MB 自动轮转 .old）

## 应用图标

`app.ico`（渐变圆角底 + 「译」字，256→16 多尺寸）由 `tools/make_icon.ps1` 生成，
同时用于 EXE 图标、窗口标题栏与托盘。

## 桌面安装

发布产物为单文件免安装 EXE（自带 .NET 运行时）：

```
publish\GlintWin.exe   # 连同 publish\Data\ 一起拷贝到任意目录
```

桌面快捷方式指向该 EXE 即可；开机自启：`Win+R` → `shell:startup` → 放入快捷方式。

## 更新词典

`Data\cedict.json` 来自 CC-CEDICT（约 12 万词条，含拼音，用于纠错）。重新生成：

```
python tools\convert_cedict.py
```

## 从源码构建与测试

```
dotnet build -c Release          # 需要 .NET 10 SDK（本机装在 D:\Development\dotnet-sdk-10）
dotnet run  --project tools/ParserTest    # 解析器单元测试（真实 OCR 乱码回归用例）
dotnet run  --project tools/OcrDiag -- <截图路径>   # OCR 增强管线诊断（输出中间产物图）
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
tools\e2e_typetest.ps1           # E2E：自动打字 + 截图验证（发布前先结束 GlintWin 进程）
```

## 当前范围与已知限制（P2）

- **搜狗输入法**：完整支持（窗口锚定，全应用可用）。
- **微软拼音**：已支持（光标锚定 OCR），限制：应用需暴露 Win32 光标
  （记事本/WinForms/多数原生应用可用；Chromium/自绘光标应用如 Chrome、VS Code 暂不支持）。
- 释义取第一候选（高亮项）；跟随翻页选中项在 P3。
- 拼音纠错：搜狗通道依赖拼音框可读性（艺术字皮肤下保守不触发）；微软拼音通道用键盘拼音，稳定可靠。
- 个别艺术字皮肤对比度过低时取色会回退深色主题。
- 管理员权限窗口无法截图（Windows 会话隔离），暂不支持。
