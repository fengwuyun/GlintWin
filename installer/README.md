# Windows 安装包

使用 Inno Setup 编译安装包，输入必须是对应版本已验证的自包含单文件 EXE：

```powershell
ISCC.exe /DAppExe="C:\path\GlintWin-v0.4.3-win-x64.exe" /DOutputPath="C:\output" installer\GlintWin.iss
```

当前版本0.4.3，默认按用户安装到 `%LOCALAPPDATA%\Programs\GlintWin`，提供开始菜单入口、可选桌面快捷方式及卸载入口，不要求管理员权限。升级前先从托盘正常退出旧版。卸载不清理 `%LOCALAPPDATA%\GlintWin` 中的个人数据。

验证：独立目录静默安装，安装后EXE的SHA256与便携版相同，运行`--verify-package`自检成功，静默卸载移除程序，现有用户配置哈希不变。未签名的安装包可能触发Windows来源提示。
