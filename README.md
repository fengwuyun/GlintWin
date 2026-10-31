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
轮询搜狗候选窗口(SoPY_Comp, 40ms) → 窗口区域截图 → 像素哈希去重（只哈希候选行区域，
避开光标闪烁/皮肤动画；混入窗口坐标防旧位置残留）
→ 2x 放大后系统离线 OCR(zh-Hans-CN) → 词级坐标按间隙切分候选块（自适应 DPI）
→ 候选行像素采样皮肤配色（对比度归一）→ CC-CEDICT 查询（整词 → 前缀切分 → 拼音交叉验证纠错）
→ 胶囊悬浮条贴在候选行正下方（永远不翻到上方）
```

- 外观自适应输入法皮肤：悬浮条背景/文字色/字号直接采样自当前皮肤的候选行。
- 悬浮条绝不抢焦点（NOACTIVATE）且鼠标穿透，不影响打字。
- 防闪烁：主题色/字号指数平滑、变化低于阈值不重绘、解析偶发失败 500ms 保持原样、
  首显需连续两帧一致。
- 释义格式：取 CC-CEDICT 前两个释义，去掉 "to " 前缀，用 ` · ` 连接；
  整词查不到时按最长前缀切分（≤6 字）；OCR 疑似错字时用组词拼音交叉验证纠正（宁缺勿错）。

## 配置（可选）

在 exe 同目录放 `config.json`（不加则用默认值）：

```json
{
  "PollIntervalMs": 40,
  "OffsetYPx": 4,
  "FontSizeDip": 15,
  "CandidateWindowClass": "SoPY_Comp",
  "OcrLanguage": "zh-Hans-CN",
  "DictionaryPath": "Data/cedict.json"
}
```

日志：`%LOCALAPPDATA%\GlintWin\logs\glint-YYYYMMDD.log`（超 5MB 自动轮转 .old）

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

## 当前范围与已知限制（P1 MVP）

- 仅支持**搜狗输入法**（候选窗口 `SoPY_Comp`）；微软拼音候选窗藏在系统「输入体验」宿主中，按方案 P2 用光标锚定 OCR/UIA 跟进。
- 释义取第一候选（高亮项）；跟随翻页选中项在 P3。
- 拼音纠错依赖拼音框可读性：艺术字皮肤下 OCR 可能读不准拼音，此时纠错保守不触发（宁缺勿错）。
- 个别艺术字皮肤对比度过低时取色会回退深色主题。
- 管理员权限窗口无法截图（Windows 会话隔离），暂不支持。
