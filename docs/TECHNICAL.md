# 技术文档 — GlintWin

> 版本 1.0 ｜ 技术栈：C# / .NET 10 / WPF + WinForms 混合 ｜ 单文件自包含发布

## 1. 总体架构

```
┌──────────────────────────── GlintWin 常驻进程 ────────────────────────────┐
│                                                                            │
│  通道层（输入源）                 公共管线                        表现层    │
│  ┌──────────────────┐   WatchEvent   ┌──────────────────────┐  ┌─────────┐ │
│  │ CandidateWatcher │──(窗口rect)──▶│ Coordinator 消费队列  │─▶│ Overlay │ │
│  │  搜狗 SoPY_Comp   │              │ 截图→哈希去重→2x OCR  │  │ Window  │ │
│  └──────────────────┘              │ →词间隙切块→皮肤取色  │  │ (WPF)   │ │
│  ┌──────────────────┐   WatchEvent │ →查词典/纠错→更新     │  └─────────┘ │
│  │ MspWatcher       │──(光标条带)──▶└──────────────────────┘              │
│  │  微软拼音(光标锚定)│                    ▲                              │
│  └──────────────────┘                    │                                │
│  ┌──────────────────┐                   │ CurrentPinyin                   │
│  │ PinyinTracker    │───────────────────────────────────────────────── │ │
│  │  LL 键盘钩子拼音   │                                                  │ │
│  └──────────────────┘                                                  │ │
│  托盘(NotifyIcon) / 配置(Config) / 日志(Logger) / 词典(CedictDictionary)◀─┘ │
└────────────────────────────────────────────────────────────────────────────┘
```

## 2. 双通道设计（关键决策）

### 2.1 搜狗通道（窗口锚定）
- 搜狗 16.9 的候选窗口是顶层窗口 `SoPY_Comp`，组词期间 `IsWindowVisible=True`，rect 紧贴光标。
- 轮询 40ms：EnumWindows → 类名过滤 → GetWindowRect → rect 变化即投递事件。

### 2.2 微软拼音通道（光标锚定 OCR）
尖峰实测（probe_msp4，Win11 25H2）：
- 候选窗由「Windows 输入体验」(TextInputHost.exe) 以 **DirectComposition** 渲染：
  无 Win32 子窗口、UIA 子树恒为 0、结构化读取不可行。
- **光标坐标可用**：GetGUIThreadInfo 在合成期间稳定返回屏幕坐标。

因此采用：**键盘拼音追踪提供"正在组词"信号 → 光标锚定条带截图 → 复用公共 OCR 管线**。
- 触发条件（三闸门）：拼音追踪器 1500ms 内有输入 + IME 处于开启状态（WM_IME_CONTROL 查询）
  + 搜狗候选窗不可见（双通道仲裁）+ 前台进程不是自己。
- 条带：光标下方 (宽 560 × 高 120)；贴近屏幕底部时改取上方。
- 限制：应用需暴露 Win32 光标（Chromium 自绘光标不支持，见 §7）。

### 2.3 仲裁
`CandidateWatcher.SogouCandidateVisible()` 供 MspWatcher 轮询调用：搜狗候选窗可见期间
MSP 通道完全静默，避免双源竞争同一悬浮条。

## 3. 公共管线（Coordinator）

数据流：`WatchEvent(rect) → 250ms 限频 → 区域截图(BGRA) → 像素哈希去重 → 2x 放大 OCR →
解析 → 皮肤取色 → 查词典/纠错 → UI 线程更新悬浮条`

关键机制：
1. **哈希防闪烁**：只哈希候选行区域（上一次 OCR 的行 rect，位图坐标），并混入窗口坐标
   （同词异位场景强制刷新）。拼音框光标闪烁、皮肤动画都在行外，不触发重 OCR。
2. **2x 放大**：候选行文字仅 15-20 物理像素，HighQualityBicubic 放大后 OCR 准确率显著提升。
3. **词间隙切块**（CandidateParser.ExtractChunksFromRow）：OCR 词级坐标按 X 排序，
   间隙 > max(6px, 字高×0.3) 即切块；块 0 = 第一候选。序号"1."被高亮吞掉时依然准确。
   文本级兜底 ExtractTop 处理无词坐标场景。
4. **皮肤取色**：候选行区域 4096 桶量化直方图；最高频=背景色，频率×对比度最高=文字色；
   对比度归一（EnsureContrast）+ 指数平滑（EMA k=0.5），主题差异 <30 不重绘。
5. **防闪烁**：主题色/字号 EMA、内容与主题变化低于阈值不重绘、解析偶发失败 500ms 保持原样、
   首显需连续两帧一致、行锚点缺失时沿用上一帧。

## 4. 词典与纠错（CedictDictionary）

- 数据：CC-CEDICT（MDBG，CC-BY-SA 4.0）→ `tools/convert_cedict.py` 生成
  `Data/cedict.json`（简体 → {p: 带调拼音, s: [释义…]}，12.1 万条，~10MB）。
- 启动全量加载内存哈希（<1s），查询 <0.1ms。
- 查询链：整词精确 → 最长前缀切分（≤6 字，块间用 "/" 连接）→ 拼音交叉验证纠错。
- 纠错（FuzzyCorrect）：等长、编辑距离 ≤1、拼音前缀兼容（PinyinCompatible）→ 取序最小者。
  拼音来源优先级：**键盘钩子实时拼音**（微软拼音通道，权威）> OCR 拼音行（搜狗通道，
  艺术字皮肤下可能不可读）。
- 验证闸门（strict 通道）：候选词的词典拼音必须与键盘拼音前缀兼容，否则视为正文误报拒绝。

## 5. 键盘拼音追踪器（PinyinTracker）与隐私边界

- WH_KEYBOARD_LL 低级钩子，专用后台线程 + 消息泵；回调仅处理 WM_KEYDOWN/SYSKEYDOWN。
- 状态机：A-Z → 追加字母（≤24）；Backspace → 删尾；Space/Enter/Esc 或 Ctrl/Alt → 清空。
- 新鲜度：1500ms。不记录修饰键、功能键、其他字符；**不持久化、不上传**（隐私红线见 PRD §4）。
- 工程约束：回调绝不抛异常/阻塞（LL 钩子 >300ms 会被系统静默摘除）。

## 6. 表现层（OverlayWindow）

- WPF 无边框透明窗 + SetWindowLong 追加 `WS_EX_NOACTIVATE|WS_EX_TOOLWINDOW|WS_EX_TRANSPARENT`
  （不抢焦点、不进 Alt-Tab、鼠标穿透）。
- "隐藏"实现为移到屏幕外（-30000），避免 Show/Hide 闪烁。
- 定位：词级坐标候选行 rect（物理像素）→ 行底 + 4px；越界横向收拢；**纵向只在屏幕底边内收拢，
  绝不翻到候选行上方**（产品红线）；DIP↔物理像素经 VisualTreeHelper.GetDpi 换算。

## 7. 已知限制与技术债

| 项 | 说明 | 缓解/计划 |
|---|---|---|
| Chromium 系应用 MSP 不支持 | 无 Win32 光标 | P3：UIA TextPattern 光标范围 |
| 艺术字皮肤拼音不可读 | 搜狗通道拼音 OCR 受限 | 键盘拼音已覆盖；Sogou 增强识别保留 |
| 正文编号列表偶发误报 | OCR 路线本质限制 | ≥2 序号闸门 + 键盘拼音交叉验证已大幅收敛 |
| 提权窗口不可见 | 会话隔离 | 明示不支持；P3 评估 UIAccess |
| 词典命中依赖 CC-CEDICT | 未登录词无释义 | 显示拼音；P3 ECDICT/网络可选项（默认关） |

## 8. 测试策略

- 单元：`tools/ParserTest`（21 用例，真实 OCR 乱码回归 + MSP 空格分隔格式 + 词级切块）。
- 诊断：`tools/OcrDiag`（增强管线中间产物导出）。
- E2E：`tools/e2e_typetest.ps1`（搜狗）/ `tools/e2e_msp.ps1`（微软拼音，日志 oracle）。
- 验收：`docs/ACCEPTANCE.md`。

## 9. 构建与发布

```
dotnet build  -c Release
dotnet publish -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
```
- 无第三方 NuGet 依赖（WinRT 经 net10.0-windows TFM 内建投影；托盘用 WinForms NotifyIcon）。
- 注意：主工程 csproj 已排除 `tools\**`（子工程）；发布前需结束运行中的 GlintWin 进程（文件锁）。
