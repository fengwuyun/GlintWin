# 「Glint 式」双语输入辅助插件 — Windows 技术方案

> 目标体验（对标 macOS 应用 Glint）：**不替换现有输入法**，在输入法候选词旁边增加一条英文释义悬浮条——
> 打「学习」时，候选栏下方自动出现 `learn · study`。本地处理、不联网、不读取正文。

---

## 0. 结论先行（本机已实测验证）

| 路线 | 搜狗输入法 16.9（优先） | 微软拼音（兜底） |
|---|---|---|
| 候选窗口发现 | ✅ **已验证**：候选窗口类名 `SoPY_Comp`，组词期间 `IsWindowVisible=True`，rect 紧贴光标下方 | ⚠️ 待验证：候选 UI 藏在「Windows 输入体验」宿主（TextInputHost.exe），常规 UIA 遍历为 0 元素 |
| 候选内容读取 | ✅ **已验证**：对窗口 rect 区域截图 + 离线 OCR（Windows.Media.Ocr，本机已装 zh-Hans-CN 识别包），实测完整读出「nihao'shijie / 1.你好世界 2.你好 3.你号 4.拟好 5.倪好」 | ⚠️ 计划用「光标位置锚定 + 同样的截图 OCR」实现，需 P2 阶段尖峰验证 |
| IMM32 组合串（GCS_COMPSTR） | ❌ 已排除：Win11 下搜狗 TSF 不回填，恒为空 | ❌ 已排除：恒为空 |
| 编辑框文本 / UIA Value 读组词 | ❌ 已排除（搜狗组词期间不写入编辑控件） | ⚠️ 现象上有希望（曾观察到组词文本出现在控件 UIA Name 中），列为 P2 尖峰 |

**推荐路线：窗口监视 + 区域截图 + 离线 OCR + 本地词典**。搜狗 P1 先行（用户优先级），微软拼音 P2 跟进。
网上没有找到 Windows 平台的现成同类工具（Glint 为 macOS 专属），需要自研。

---

## 1. 体验定义（MVP 验收标准）

1. 用户在任意应用中用搜狗打拼音，候选栏出现时，其**正下方**出现一条深色圆角悬浮条（类似截图中的 `learn · study`）。
2. 悬浮条显示**当前第一候选词的英文释义**（取词典前 1–2 个释义，如「学习」→ `learn · study`）。
3. 候选变化（拼音增删、翻页）时悬浮条内容实时更新；候选栏消失（上屏/取消）后 100ms 内隐藏。
4. 全程离线：本地词典查询，OCR 用系统自带引擎，不发起任何网络请求。
5. 常驻托盘，可暂停/退出；开机自启可选。

## 2. 已完成的可行性验证（2026-11-01，本机 Win11 25H2）

探测脚本与证据都在 `D:\Development\ZCode_workplace\.ime-probe\`：

| 验证项 | 结果 | 证据 |
|---|---|---|
| 搜狗候选窗口载体 | `SoPY_Comp` 顶层窗口，组词时 vis=True，rect 如 `(164,209)-(762,361)` | probe_v5.ps1 输出 |
| 窗口区域截图可读性 | 裁剪图完整呈现拼音 `nihao'shijie` 与 5 个候选词 | `t1a_crop0.png` |
| 离线 OCR 中文 | `Windows.Media.Ocr` AvailableRecognizerLanguages 含 **zh-Hans-CN** | probe_v5.ps1 输出 |
| IMM32 组合串 | 搜狗/微软拼音 GCS_COMPSTR 均为空 → 排除 | probe_v3/v4/v5 |
| 编辑控件文本/UIA | 搜狗组词期间为空 → 排除（微软拼音疑似可读，P2 验证） | probe_v5 |
| 本机环境 | 搜狗 16.9（`D:\Application\SogouInput\16.9.0.5523`）+ 微软拼音并存；.NET 与 Python 均可用 | probe_env.ps1 |

> 注：探测中 Win+Space 合成切换输入法后偶发按键焦点丢失，属测试脚本问题，不影响结论；
> 探测结束后已将输入法切回，如发现不在搜狗，按一下 `Win+空格` 即可。

## 3. 系统架构

```
┌──────────────────────────  常驻进程  GlintWin（C#/.NET 8 WPF）──────────────────────────┐
│                                                                                          │
│  ①窗口监视器          ②采集识别器                ③词典引擎              ④悬浮条 UI      │
│  轮询/事件监听   →    BitBlt 区域截图        →    内存哈希表查询     →   无边框置顶窗口    │
│  SoPY_Comp 可见性      Windows.Media.Ocr          CC-CEDICT 简体→英文     贴候选栏下方    │
│  (30~60ms 周期)        (zh-Hans-CN, 本地)         最长匹配→逐字兜底      毛玻璃/深色样式  │
│       │                     │                          ▲                                  │
│       │ 变化时才触发          │ 像素哈希去重(未变不OCR)     │ 启动时全量加载(~1s)              │
│       ▼                     ▼                          │                                  │
│   Win32: EnumWindows   GDI: GetWindowRect→裁剪     data/cc-cedict.db                       │
│   GetWindowRect        OCR: ≤30ms/次                (启动解析, 常驻内存)                    │
└──────────────────────────────────────────────────────────────────────────────────────────┘
```

数据流（每一次候选变化）：
`SoPY_Comp rect 变化 → 区域截图 → 像素哈希对比（相同则跳过）→ OCR → 正则解析候选行「1.xxx 2.xxx…」→ 取第 1 候选（当前高亮项）→ 词典查询 → 更新悬浮条文本与位置`

### 3.1 关键技术点

**① 候选窗口监视**
- `EnumWindows` + `GetClassName` 过滤 `SoPY_Comp`（轮询 30–60ms，或 `SetWinEventHook(EVENT_OBJECT_SHOW/HIDE)` 事件驱动，MVP 用轮询足够）。
- `IsWindowVisible=True` 且 rect 宽高 > 阈值 → 判定「组词中」；窗口消失/vis=False → 隐藏悬浮条。
- 每次取 `GetWindowRect`（物理像素），换算 DPI 后用于截图与悬浮条定位。

**② 截图 + 离线 OCR**
- `BitBlt`（`CreateDCW("DISPLAY")`）按 rect 截图，DPI 感知（进程声明 PerMonitorV2）。
- `Windows.Media.Ocr.OcrEngine`（`TryCreateFromLanguage("zh-Hans-CN")`），单次 ~10–30ms。
- **像素哈希去重**：候选词没变就不重复 OCR（拼音增删、翻页才触发）。
- 解析：OCR 结果按行取「数字+.+汉字」模式还原候选列表；拼音行（如 `nihao'shijie`）可用于显示原拼音。
- 版本兼容保险：类名等参数放入 `config.json`（`SoPY_Comp`、候选行正则可配置），搜狗大版本升级时可热调。

**③ 词典（本地、离线）**
- 主词典：**CC-CEDICT**（MDBG 社区维护，约 12 万词条，免费）。
  条目格式 `繁体 简体 [拼音] /英文1/英文2/`，启动时解析成 `简体 → [释义…]` 内存哈希表（约 12 万条，<50MB，加载 <2s，查询 <0.1ms）。
  例：`学习 → to study; to learn` → 悬浮条显示 `learn · study`（取前两个动名词性释义）。
- 兜底策略（按序）：
  1. 整词精确匹配；
  2. 最长前缀切分（如「输入法」→ 输入 + 法 组合释义）；
  3. 逐字释义拼接（CC-CEDICT 收录单字）；
  4. 查不到 → 悬浮条显示拼音（OCR 已获得），显示「—」。
- 可选增强（P3）：ECDICT（77 万+ 英汉条目）补词性/词频/CEFR 标签；用户自补词库（JSON 追加）。

**④ 悬浮条 UI**
- WPF `Window`：`WindowStyle=None`、`ShowActivated=false`、`Topmost=true`、`WS_EX_NOACTIVATE|WS_EX_TOOLWINDOW`（绝不抢焦点、不进 Alt-Tab、不影响输入）。
- 位置：候选栏 rect 下方 8px（同 Glint）；超出屏幕自动翻转至上侧。
- 样式：深色圆角胶囊、浅色文字，参考 Glint 截图；透明度/字号/偏移可配置。

**⑤ 性能与稳定性**
- 端到端预算 < 80ms（rect 变化 → 悬浮条更新）：截图 5ms + OCR 30ms + 查词 0.1ms + 渲染 10ms。
- 单线程消费者队列（监视器投递 rect，工作线程串行 OCR），避免抖动时叠加截图。
- 单实例互斥（命名 Mutex）；崩溃隔离：OCR/词典任何异常都不影响输入法本身（只读，零注入）。

## 4. 技术选型

| 项 | 选择 | 理由 |
|---|---|---|
| 语言/框架 | **C# / .NET 8，WPF** | Win32 互操作、PerMonitorV2 DPI、系统 OCR（CsWinRT）调用都最顺；单文件发布 |
| 截图 | GDI BitBlt（MVP）→ Windows.Graphics.Capture（P3 可选） | BitBlt 对静态候选栏足够、实现最简 |
| OCR | Windows.Media.Ocr（系统自带，离线） | 零依赖、10–30ms；备选 PaddleOCR（更准但重 ~100MB，仅在识别率不达标时引入） |
| 词典 | CC-CEDICT 内存哈希 | 离线、免费、格式规整 |
| 托盘 | H.NotifyIcon.Wpf | 成熟 |
| 备选原型 | Python（pywin32 + Pillow + rapidocr） | 若想先一晚验证手感可用 Python 做 P0 原型，正式版仍建议 C# |

## 5. 分阶段实施计划

### P1 — 搜狗 MVP（预计 2~4 个晚上）
- [ ] 工程骨架：托盘 + 配置文件 + 日志
- [ ] `SoPY_Comp` 监视器（轮询 + rect 提取 + 显示/隐藏状态机）
- [ ] 区域截图 + Windows.Media.Ocr + 候选行解析
- [ ] CC-CEDICT 下载/转换脚本（Python 一次性生成 `cedict.json` 或精简二进制）+ 内存查询
- [ ] WPF 悬浮条（定位/隐藏/防抢焦点）
- [ ] 测试：记事本、VS Code、Chrome 地址栏、微信、Office（重点看 DPI 与焦点行为）

### P2 — 微软拼音支持（预计 1~2 个晚上 + 尖峰）
- [ ] 尖峰 A：组词时用 Accessibility Insights 检查「Windows 输入体验」UIA 树，确认候选列表是否可结构化读取（若可 → 微软拼音走 UIA 精读，体验最佳）
- [ ] 尖峰 B（UIA 不通时）：以「前台线程光标 rcCaret（GetGUIThreadInfo）」为锚点 + 检测 IME 开启状态（`ImmGetDefaultIMEWnd` + `WM_IME_CONTROL/IMC_GETOPENSTATUS`）→ 光标下方区域 OCR
- [ ] 双输入法自动适配（检测当前 TIP：搜狗走窗口锚定，微软拼音走光标锚定/UIA）

### P3 — 打磨（按需）
- [ ] 跟随「当前选中候选」而非固定第 1 候选（OCR 高亮色块定位，或 1–5 数字键热键切换释义）
- [ ] 设置界面：字号/偏移/主题/暂停快捷键/开机自启/应用黑名单
- [ ] ECDICT 增强（词性、频率、例句）、用户自定义词库
- [ ] Windows.Graphics.Capture 替代 BitBlt（更省电）；UAC 提权窗口支持（UIAccess 或以管理员运行选项）

## 6. 风险与对策

| 风险 | 影响 | 对策 |
|---|---|---|
| 搜狗升级改内部窗口类名 | 监视失效 | 类名/正则进配置文件；监视器零命中持续 N 分钟时托盘提示 |
| OCR 误识别（艺术字皮肤、低对比） | 释义错词 | 只裁候选行区域；置信度阈值 + 词典反查校正（OCR 结果若不在词典，尝试编辑距离 ≤1 的词条）；必要时引入 PaddleOCR |
| 高 DPI / 多屏 / 缩放 | 截图偏移、悬浮条错位 | 进程 PerMonitorV2；全部物理像素计算后统一换算 |
| 管理员权限窗口 | 无法截图 | MVP 明示不支持；P3 提供 UIAccess/提权运行 |
| 性能（高频率翻页） | CPU 占用 | 像素哈希去重 + 80ms 节流 + 单队列 |
| 隐私 | 用户顾虑 | 全离线、不装键盘钩子、不注入任何进程、源码可审计（README 声明） |

## 7. 测试矩阵

| 维度 | 用例 |
|---|---|
| 输入法 | 搜狗 16.9（全拼）、微软拼音；后续：双拼、五笔（P3 评估，OCR 路线天然兼容） |
| 应用 | 记事本 / VS Code / Chrome / Edge / 微信 / Word（UAC 内外各一） |
| 显示 | 100% / 125% / 150% 缩放；单屏 + 双屏混合；深色皮肤候选栏 |
| 行为 | 长句组词、翻页（+/−）、中英混输、Shift 切英文、ESC 取消、候选上屏 |
| 词典 | 常用词 / 未登录词（人名）/ 成语 / 单字 |

## 8. 附录

- 探测脚本：`.ime-probe/probe_env.ps1`（环境）、`probe_uia.ps1`、`probe_v3/v4/v5.ps1`（候选窗口与数据通道）
- 关键证据图：`.ime-probe/t1a_crop0.png`（搜狗候选栏截图 OCR 素材）、`t2a_full.png`（微软拼音对照）
- 复现：`powershell -NoProfile -ExecutionPolicy Bypass -File .ime-probe\probe_v5.ps1`（会自动打开测试窗口输入，结束自关）

### 参考资料
- Glint（对标产品，macOS）：输入中文顺手看见英文，「不替换输入法，只在候选旁多给一眼」
- iOS 同类（证明交互价值）：[中英输入法-打字学英语](https://apps.apple.com/ca/app/id6756459946)
- 词典：CC-CEDICT（mdbg.net/chinese/dictionary?page=cc-cedict）、[ECDICT](https://github.com/skywind3000/ECDICT)
- OCR：[Windows.Media.Ocr 文档](https://learn.microsoft.com/uwp/api/windows.media.ocr.ocrengine)
- Windows 输入框架：[TSF 概述](https://learn.microsoft.com/windows/win32/tsf/text-services-framework)
