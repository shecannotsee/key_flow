Unicode true
!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "x64.nsh"
!ifndef VERSION
!define VERSION "1.2.0"
!endif
Name "KeyFlow"
OutFile "${OUTPUT}"
InstallDir "$PROGRAMFILES64\KeyFlow"
InstallDirRegKey HKLM "Software\KeyFlow" "InstallDir"
RequestExecutionLevel admin
ManifestDPIAware true
SetCompressor /SOLID lzma
VIProductVersion "${VERSION}.0"
VIAddVersionKey "ProductName" "KeyFlow"
VIAddVersionKey "FileDescription" "KeyFlow 安装程序"
VIAddVersionKey "FileVersion" "${VERSION}"
VIAddVersionKey "LegalCopyright" "KeyFlow"
!define MUI_ICON "${ICON}"
!define MUI_UNICON "${ICON}"
!define MUI_ABORTWARNING
!define MUI_WELCOMEPAGE_TITLE "欢迎安装 KeyFlow"
!define MUI_WELCOMEPAGE_TEXT "安装 KeyFlow 按键工具。$\r$\n$\r$\n安装完成后，打开程序并点击“准备使用”。$\r$\n$\r$\n配置与日志保存在用户数据目录，更新和卸载默认保留。"
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_COMPONENTS
!insertmacro MUI_PAGE_INSTFILES
!define MUI_FINISHPAGE_TEXT "KeyFlow 已安装。可通过桌面或开始菜单打开。首次打开后，点击“准备使用”。"
!insertmacro MUI_PAGE_FINISH
!define MUI_UNCONFIRMPAGE_TEXT_TOP "卸载 KeyFlow 程序及快捷方式。个人配置、日志和系统共享输入组件将保留。"
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_UNPAGE_FINISH
!insertmacro MUI_LANGUAGE "SimpChinese"
!insertmacro MUI_LANGUAGE "English"

Function .onInit
  ${IfNot} ${RunningX64}
    MessageBox MB_ICONSTOP "KeyFlow requires 64-bit Windows."
    Abort
  ${EndIf}
  SetRegView 64
  SetShellVarContext all
FunctionEnd

Section "KeyFlow（必需）" MainSection
  SectionIn RO
  InitPluginsDir
  SetOutPath "$PLUGINSDIR"
  File "${HELPER}"
  nsExec::ExecToStack 'powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "$PLUGINSDIR\Prepare-Installation.ps1" -CheckRunningOnly'
  Pop $0
  Pop $1
  ${If} $0 != 0
    MessageBox MB_ICONSTOP "请先关闭 KeyFlow，再重试。$\r$\n$1" /SD IDOK
    Abort
  ${EndIf}
  SetOutPath "$INSTDIR"
  File /r "${PAYLOAD}\*"
  File /oname=keyflow-${VERSION}.ico "${ICON}"
  WriteUninstaller "$INSTDIR\Uninstall.exe"
  WriteRegStr HKLM "Software\KeyFlow" "InstallDir" "$INSTDIR"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\KeyFlow" "DisplayName" "KeyFlow"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\KeyFlow" "DisplayVersion" "${VERSION}"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\KeyFlow" "Publisher" "KeyFlow"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\KeyFlow" "DisplayIcon" "$INSTDIR\keyflow-${VERSION}.ico,0"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\KeyFlow" "InstallLocation" "$INSTDIR"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\KeyFlow" "UninstallString" '"$INSTDIR\Uninstall.exe"'
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\KeyFlow" "QuietUninstallString" '"$INSTDIR\Uninstall.exe" /S'
  WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\KeyFlow" "NoModify" 1
  WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\KeyFlow" "NoRepair" 1
  nsExec::ExecToStack 'powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "$INSTDIR\Prepare-Installation.ps1"'
  Pop $0
  Pop $1
  ${If} $0 != 0
    MessageBox MB_ICONSTOP "所需组件未就绪，请重启电脑后重新安装。程序已复制，可从系统设置卸载。$\r$\n$1" /SD IDOK
    SetErrorLevel 1
    Abort
  ${EndIf}
SectionEnd
Section "桌面快捷方式" DesktopSection
  CreateShortCut "$DESKTOP\KeyFlow.lnk" "$INSTDIR\KeyFlow.exe" "" "$INSTDIR\keyflow-${VERSION}.ico" 0
SectionEnd
Section "开始菜单快捷方式" MenuSection
  CreateDirectory "$SMPROGRAMS\KeyFlow"
  CreateShortCut "$SMPROGRAMS\KeyFlow\KeyFlow.lnk" "$INSTDIR\KeyFlow.exe" "" "$INSTDIR\keyflow-${VERSION}.ico" 0
  CreateShortCut "$SMPROGRAMS\KeyFlow\卸载 KeyFlow.lnk" "$INSTDIR\Uninstall.exe" "" "$INSTDIR\keyflow-${VERSION}.ico" 0
SectionEnd
Section -Refresh
  System::Call 'shell32::SHChangeNotify(i 0x08000000, i 0, p 0, p 0)'
SectionEnd
Function un.onInit
  SetRegView 64
  SetShellVarContext all
FunctionEnd
Section "Uninstall"
  nsExec::ExecToStack 'powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "$INSTDIR\Prepare-Installation.ps1" -CheckRunningOnly'
  Pop $0
  Pop $1
  ${If} $0 != 0
    MessageBox MB_ICONSTOP "请先关闭 KeyFlow，再卸载。$\r$\n$1" /SD IDOK
    Abort
  ${EndIf}
  Delete "$DESKTOP\KeyFlow.lnk"
  Delete "$SMPROGRAMS\KeyFlow\KeyFlow.lnk"
  Delete "$SMPROGRAMS\KeyFlow\卸载 KeyFlow.lnk"
  RMDir "$SMPROGRAMS\KeyFlow"
  !include "${REMOVE_LIST}"
  Delete "$INSTDIR\keyflow-${VERSION}.ico"
  Delete "$INSTDIR\Uninstall.exe"
  RMDir "$INSTDIR"
  DeleteRegKey HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\KeyFlow"
  DeleteRegKey HKLM "Software\KeyFlow"
  System::Call 'shell32::SHChangeNotify(i 0x08000000, i 0, p 0, p 0)'
SectionEnd
