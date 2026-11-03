#ifndef AppExe
  #error AppExe must point to the verified GlintWin.exe
#endif
#ifndef OutputPath
  #define OutputPath "."
#endif
[Setup]
AppId={{195B6217-31F0-4B21-A9FC-DCBB8A43C240}
AppName=GlintWin
AppVersion=0.4.4
AppPublisher=GlintWin
AppPublisherURL=https://github.com/fengwuyun/GlintWin
AppSupportURL=https://github.com/fengwuyun/GlintWin/issues
DefaultDirName={localappdata}\Programs\GlintWin
DefaultGroupName=GlintWin
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
OutputDir={#OutputPath}
OutputBaseFilename=GlintWin-v0.4.4-setup-r2-win-x64
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\GlintWin.exe
AppMutex=GlintWin_SingleInstance
CloseApplications=yes
RestartApplications=no
SetupLogging=yes
[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "chinesesimplified"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"
[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
[Files]
Source: "{#AppExe}"; DestDir: "{app}"; DestName: "GlintWin.exe"; Flags: ignoreversion
[Icons]
Name: "{group}\GlintWin"; Filename: "{app}\GlintWin.exe"
Name: "{autodesktop}\GlintWin"; Filename: "{app}\GlintWin.exe"; Check: ShouldCreateDesktopShortcut
Name: "{userstartup}\GlintWin"; Filename: "{app}\GlintWin.exe"; Parameters: "-tray"; Check: HasStartupShortcut
[Run]
Filename: "{app}\GlintWin.exe"; Description: "{cm:LaunchProgram,GlintWin}"; Flags: nowait postinstall skipifsilent
; Deliberately no UninstallDelete for user data in LocalAppData\GlintWin.

[Code]
function HasStartupShortcut: Boolean;
begin
  Result := FileExists(ExpandConstant('{userstartup}\GlintWin.lnk'));
end;

function ShouldCreateDesktopShortcut: Boolean;
begin
  Result := WizardIsTaskSelected('desktopicon') or FileExists(ExpandConstant('{autodesktop}\GlintWin.lnk'));
end;