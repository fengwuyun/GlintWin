# GlintWin 0.4 · 运行与每日词汇

## 运行方式
发布版为 Windows x64 自包含单文件 EXE，内含 .NET 10 运行时、基础词典、主题词包与图标；双击运行，无需另外安装 .NET，也不需要随程序携带 Data 文件夹。它是免安装版，不是安装向导。
要求 Windows 10 2004（19041）或更新版本、支持的中文输入法，以及系统中文 OCR 能力。程序依赖 Windows OCR，不能把“内含运行时”理解成“不需要系统 OCR 语言组件”。状态总览会显示 OCR 是否就绪。微软拼音通道仍受应用是否暴露 Win32 光标限制。

首次运行会把内置资源释放至 `%LOCALAPPDATA%\GlintWin\`。升级时，仅在目标文件不存在时迁移 EXE 旁旧版 `config.json`、`Data`，保留原文件且不覆盖已有用户配置。之后配置、词库和每日数据库统一使用用户目录，适用于 EXE 所在目录不可写的情形。便携 EXE 的新副本仍共享同一 Windows 用户的数据。

## 每日词汇
控制台 → 每日词汇 → 自动采集，默认关闭；启用后本地保存，经 OCR 推断的选词全部标记“待核对”。托盘暂停同步暂停采集，可填写进程名排除应用。
- 支持：空格选择稳定首候选；数字选择仅在 OCR 序号连续且候选对应关系明确时采集。
- 不采集：Esc 取消、过期/失焦/拼音已变化的快照、无可靠对应关系的数字选择。回车可能直接提交拼音，因此不把它视为中文候选确认。鼠标选词、翻页后无法可靠匹配的结果跳过。
- 不是全量键盘或实际上屏文本记录；不保存原始按键、截图、应用正文，不上传。
- 同日同词合并次数，跨日独立；默认按最近出现排序，也可按出现次数排序。按日期、状态、文本筛选，每页50条。
- 选择词条，核对中文、拼音和释义后“确认并保存”；可忽略、导出筛选结果，或加入自定义词库并立即重载。覆盖已有自定义词条需要确认。
- 删除日期会删除该日采集记录，需确认；不会删除已加入自定义词库的词。记录保存在 `daily-words.db`，不自动上传、不自动清理。
- 写入采用最多2048项队列，过载时丢弃新采集并在页面显示异常；不会无限积压或阻塞键盘等待磁盘。

## 定位改进
使用候选窗口内部的稳定行偏移，不再在行识别失败时跳到整个皮肤窗口下方。容忍小幅 OCR 抖动，布局突变需要两个一致观测；短暂失败保持锚点，过期隐藏。窗口移动后按新窗口位置换算；输入法通道切换清空旧状态，忽略旧通道隐藏事件与过期 OCR 帧。

## 验证命令
使用 .NET10 SDK，运行 tools/PositionTest、tools/DailyTest、tools/ConfigTest、tools/ParserTest、tools/UiTest。UiTest 的参数为截图输出目录，只使用独立测试数据。
发布：`dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish`。
独立包检查：只复制发布 EXE 到空目录，运行 `GlintWin.exe --verify-package <独立检查目录>`；生成 `package-check.json`，检查内置资源释放、基础词条数量与本机 OCR 可用性，不启动键盘钩子。该检查不能替代在未安装中文 OCR 的干净 Windows 虚拟机上验收。

基础词典来自 CC-CEDICT（CC-BY-SA 4.0）：https://cc-cedict.org/ 。
运行时说明：https://learn.microsoft.com/dotnet/core/deploying/single-file/
OCR API：https://learn.microsoft.com/uwp/api/windows.media.ocr.ocrengine.trycreatefromlanguage
