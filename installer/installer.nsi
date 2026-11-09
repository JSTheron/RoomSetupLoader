Unicode true
!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "x64.nsh"
!include "nsDialogs.nsh"

!define APPNAME "Room Setup Loader"
!define APPVER "1.41"
!define REGKEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\RoomSetupLoader"

Name "${APPNAME}"
OutFile "..\RoomSetupLoader-Setup-v${APPVER}.exe"
InstallDir "$PROGRAMFILES64\Room Setup Loader"
RequestExecutionLevel admin
SetCompressor /SOLID lzma
ShowInstDetails show
ShowUninstDetails show

!define MUI_ICON "..\assets\RoomSetupLoader.ico"
!define MUI_UNICON "..\assets\RoomSetupLoader.ico"
!define MUI_WELCOMEFINISHPAGE_BITMAP "..\assets\RSL-Installer-Welcome.bmp"
!define MUI_UNWELCOMEFINISHPAGE_BITMAP "..\assets\RSL-Installer-Welcome.bmp"

!insertmacro MUI_PAGE_WELCOME
; Informational acknowledgement before installation settings.
!define MUI_LICENSEPAGE_CHECKBOX
!define MUI_LICENSEPAGE_CHECKBOX_TEXT "I have read and acknowledge this notice"
!insertmacro MUI_PAGE_LICENSE "..\ABOUT-AND-DISCLAIMER.txt"
!insertmacro MUI_PAGE_DIRECTORY
Page custom ConfigPageCreate ConfigPageLeave
Page custom ConnectionPageCreate ConnectionPageLeave
Page custom LoggingPageCreate LoggingPageLeave
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
UninstPage custom un.RemoveDataPageCreate un.RemoveDataPageLeave
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "English"

Var ConfigIni
Var ConfigScript
Var AtemIp
Var ImageFolder
Var ImagePrefix
Var ImageWidth
Var ImageHeight
Var FieldIp
Var FieldFolder
Var FieldPrefix
Var FieldWidth
Var FieldHeight
Var LogRetentionDays
Var FieldLogRetentionDays
Var MaxLogFolderSizeMB
Var FieldMaxLogFolderSizeMB
Var LogCleanupIntervalHours
Var FieldLogCleanupIntervalHours
Var MaxIndividualLogSizeMB
Var FieldMaxIndividualLogSizeMB
Var AtemConnectionAttempts
Var AtemConnectionRetryDelaySeconds
Var FieldAtemConnectionAttempts
Var FieldAtemConnectionRetryDelaySeconds
Var ConfigResult
Var un.RemoveAppData
Var un.RemoveDataCheckbox

Function ConfigPageCreate
  InitPluginsDir
  StrCpy $ConfigIni "$PLUGINSDIR\settings.ini"
  StrCpy $ConfigScript "$PLUGINSDIR\Configure-Settings.ps1"
  File /oname=$PLUGINSDIR\Configure-Settings.ps1 "Configure-Settings.ps1"
  WriteINIStr "$ConfigIni" "Settings" "AtemIpAddress" "192.168.10.240"
  WriteINIStr "$ConfigIni" "Settings" "AtemConnectionAttempts" "3"
  WriteINIStr "$ConfigIni" "Settings" "AtemConnectionRetryDelaySeconds" "5"
  WriteINIStr "$ConfigIni" "Settings" "ImageFolder" "D:\Assets\ATEM"
  WriteINIStr "$ConfigIni" "Settings" "ImagePrefix" "Setup_"
  WriteINIStr "$ConfigIni" "Settings" "Width" "1920"
  WriteINIStr "$ConfigIni" "Settings" "Height" "1080"
  WriteINIStr "$ConfigIni" "Settings" "LogRetentionDays" "365"
  WriteINIStr "$ConfigIni" "Settings" "MaxLogFolderSizeMB" "1024"
  WriteINIStr "$ConfigIni" "Settings" "LogCleanupIntervalHours" "24"
  WriteINIStr "$ConfigIni" "Settings" "MaxIndividualLogSizeMB" "10"
  ; Existing machine-wide configuration takes precedence on upgrades.
  ${If} ${FileExists} "$APPDATA\Room Setup Loader\config.json"
    nsExec::ExecToLog '"$SYSDIR\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "$ConfigScript" -Mode Read -IniPath "$ConfigIni" -ConfigPath "$APPDATA\Room Setup Loader\config.json"'
    Pop $ConfigResult
    ${If} $ConfigResult != 0
      MessageBox MB_ICONSTOP "Could not read existing configuration. Check the installer details."
      Abort
    ${EndIf}
  ${EndIf}
  ReadINIStr $AtemIp "$ConfigIni" "Settings" "AtemIpAddress"
  ReadINIStr $AtemConnectionAttempts "$ConfigIni" "Settings" "AtemConnectionAttempts"
  ReadINIStr $AtemConnectionRetryDelaySeconds "$ConfigIni" "Settings" "AtemConnectionRetryDelaySeconds"
  ReadINIStr $ImageFolder "$ConfigIni" "Settings" "ImageFolder"
  ReadINIStr $ImagePrefix "$ConfigIni" "Settings" "ImagePrefix"
  ReadINIStr $ImageWidth "$ConfigIni" "Settings" "Width"
  ReadINIStr $ImageHeight "$ConfigIni" "Settings" "Height"
  ReadINIStr $LogRetentionDays "$ConfigIni" "Settings" "LogRetentionDays"
  ReadINIStr $MaxLogFolderSizeMB "$ConfigIni" "Settings" "MaxLogFolderSizeMB"
  ReadINIStr $LogCleanupIntervalHours "$ConfigIni" "Settings" "LogCleanupIntervalHours"
  ReadINIStr $MaxIndividualLogSizeMB "$ConfigIni" "Settings" "MaxIndividualLogSizeMB"
  nsDialogs::Create 1018
  Pop $0
  ${If} $0 == error
    Abort
  ${EndIf}
  ${NSD_CreateLabel} 0 0 100% 22u "Confirm application settings. Existing settings are shown on upgrades."
  Pop $0
  ${NSD_CreateLabel} 0 24u 38% 12u "ATEM IP address:"
  Pop $0
  ${NSD_CreateText} 40% 21u 60% 13u "$AtemIp"
  Pop $FieldIp
  ${NSD_CreateLabel} 0 44u 38% 12u "Image folder:"
  Pop $0
  ${NSD_CreateText} 40% 41u 60% 13u "$ImageFolder"
  Pop $FieldFolder
  ${NSD_CreateLabel} 0 64u 38% 12u "Image prefix:"
  Pop $0
  ${NSD_CreateText} 40% 61u 60% 13u "$ImagePrefix"
  Pop $FieldPrefix
  ${NSD_CreateLabel} 0 84u 38% 12u "Image width:"
  Pop $0
  ${NSD_CreateNumber} 40% 81u 60% 13u "$ImageWidth"
  Pop $FieldWidth
  ${NSD_CreateLabel} 0 104u 38% 12u "Image height:"
  Pop $0
  ${NSD_CreateNumber} 40% 101u 60% 13u "$ImageHeight"
  Pop $FieldHeight
  ${NSD_CreateButton} 68% 124u 32% 12u "Reset to defaults"
  Pop $0
  ${NSD_OnClick} $0 ResetApplicationDefaults
  nsDialogs::Show
FunctionEnd

; Only change the visible fields. Nothing is written until Next is clicked.
Function ResetApplicationDefaults
  ${NSD_SetText} $FieldIp "192.168.10.240"
  ${NSD_SetText} $FieldFolder "D:\Assets\ATEM"
  ${NSD_SetText} $FieldPrefix "Setup_"
  ${NSD_SetText} $FieldWidth "1920"
  ${NSD_SetText} $FieldHeight "1080"
FunctionEnd

Function ConfigPageLeave
  ${NSD_GetText} $FieldIp $AtemIp
  ${NSD_GetText} $FieldFolder $ImageFolder
  ${NSD_GetText} $FieldPrefix $ImagePrefix
  ${NSD_GetText} $FieldWidth $ImageWidth
  ${NSD_GetText} $FieldHeight $ImageHeight
  WriteINIStr "$ConfigIni" "Settings" "AtemIpAddress" "$AtemIp"
  WriteINIStr "$ConfigIni" "Settings" "ImageFolder" "$ImageFolder"
  WriteINIStr "$ConfigIni" "Settings" "ImagePrefix" "$ImagePrefix"
  WriteINIStr "$ConfigIni" "Settings" "Width" "$ImageWidth"
  WriteINIStr "$ConfigIni" "Settings" "Height" "$ImageHeight"
  nsExec::ExecToLog '"$SYSDIR\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "$ConfigScript" -Mode Validate -IniPath "$ConfigIni"'
  Pop $ConfigResult
  ${If} $ConfigResult != 0
    MessageBox MB_ICONEXCLAMATION "Invalid configuration. Enter a valid IPv4 address, nonempty folder/prefix, and dimensions from 1 to 16384. See installer details for more information."
    Abort
  ${EndIf}
FunctionEnd


Function ConnectionPageCreate
  nsDialogs::Create 1018
  Pop $0
  ${If} $0 == error
    Abort
  ${EndIf}
  ${NSD_CreateLabel} 0 0 100% 25u "ATEM connection retry settings. Existing values are shown on upgrades."
  Pop $0
  ${NSD_CreateLabel} 0 35u 40% 12u "Connection attempts (1-20):"
  Pop $0
  ${NSD_CreateNumber} 44% 32u 56% 13u "$AtemConnectionAttempts"
  Pop $FieldAtemConnectionAttempts
  ${NSD_CreateLabel} 0 59u 42% 20u "Retry delay (seconds, 0-300):"
  Pop $0
  ${NSD_CreateNumber} 44% 56u 56% 13u "$AtemConnectionRetryDelaySeconds"
  Pop $FieldAtemConnectionRetryDelaySeconds
  ${NSD_CreateButton} 68% 120u 32% 12u "Reset to defaults"
  Pop $0
  ${NSD_OnClick} $0 ResetConnectionDefaults
  nsDialogs::Show
FunctionEnd

; Reset only the connection page fields; commit when Next is clicked.
Function ResetConnectionDefaults
  ${NSD_SetText} $FieldAtemConnectionAttempts "3"
  ${NSD_SetText} $FieldAtemConnectionRetryDelaySeconds "5"
FunctionEnd

Function ConnectionPageLeave
  ${NSD_GetText} $FieldAtemConnectionAttempts $AtemConnectionAttempts
  ${NSD_GetText} $FieldAtemConnectionRetryDelaySeconds $AtemConnectionRetryDelaySeconds
  WriteINIStr "$ConfigIni" "Settings" "AtemConnectionAttempts" "$AtemConnectionAttempts"
  WriteINIStr "$ConfigIni" "Settings" "AtemConnectionRetryDelaySeconds" "$AtemConnectionRetryDelaySeconds"
  nsExec::ExecToLog '"$SYSDIR\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "$ConfigScript" -Mode Validate -IniPath "$ConfigIni"'
  Pop $ConfigResult
  ${If} $ConfigResult != 0
    MessageBox MB_ICONEXCLAMATION "Invalid connection settings. Attempts must be 1-20 and retry delay 0-300 seconds. See installer details."
    Abort
  ${EndIf}
FunctionEnd

Function LoggingPageCreate
  nsDialogs::Create 1018
  Pop $0
  ${If} $0 == error
    Abort
  ${EndIf}
  ${NSD_CreateLabel} 0 0 100% 22u "Configure automatic log retention and rotation."
  Pop $0
  ${NSD_CreateLabel} 0 29u 38% 12u "Retention (days):"
  Pop $0
  ${NSD_CreateNumber} 40% 26u 60% 13u "$LogRetentionDays"
  Pop $FieldLogRetentionDays
  ${NSD_CreateLabel} 0 49u 38% 12u "Max folder (MB):"
  Pop $0
  ${NSD_CreateNumber} 40% 46u 60% 13u "$MaxLogFolderSizeMB"
  Pop $FieldMaxLogFolderSizeMB
  ${NSD_CreateLabel} 0 69u 38% 12u "Cleanup interval (hours):"
  Pop $0
  ${NSD_CreateNumber} 40% 66u 60% 13u "$LogCleanupIntervalHours"
  Pop $FieldLogCleanupIntervalHours
  ${NSD_CreateLabel} 0 89u 38% 12u "Max file (MB):"
  Pop $0
  ${NSD_CreateNumber} 40% 86u 60% 13u "$MaxIndividualLogSizeMB"
  Pop $FieldMaxIndividualLogSizeMB
  ${NSD_CreateButton} 68% 120u 32% 12u "Reset to defaults"
  Pop $0
  ${NSD_OnClick} $0 ResetLoggingDefaults
  nsDialogs::Show
FunctionEnd

Function ResetLoggingDefaults
  ${NSD_SetText} $FieldLogRetentionDays "365"
  ${NSD_SetText} $FieldMaxLogFolderSizeMB "1024"
  ${NSD_SetText} $FieldLogCleanupIntervalHours "24"
  ${NSD_SetText} $FieldMaxIndividualLogSizeMB "10"
FunctionEnd

Function LoggingPageLeave
  ${NSD_GetText} $FieldLogRetentionDays $LogRetentionDays
  ${NSD_GetText} $FieldMaxLogFolderSizeMB $MaxLogFolderSizeMB
  ${NSD_GetText} $FieldLogCleanupIntervalHours $LogCleanupIntervalHours
  ${NSD_GetText} $FieldMaxIndividualLogSizeMB $MaxIndividualLogSizeMB
  WriteINIStr "$ConfigIni" "Settings" "LogRetentionDays" "$LogRetentionDays"
  WriteINIStr "$ConfigIni" "Settings" "MaxLogFolderSizeMB" "$MaxLogFolderSizeMB"
  WriteINIStr "$ConfigIni" "Settings" "LogCleanupIntervalHours" "$LogCleanupIntervalHours"
  WriteINIStr "$ConfigIni" "Settings" "MaxIndividualLogSizeMB" "$MaxIndividualLogSizeMB"
  nsExec::ExecToLog '"$SYSDIR\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "$ConfigScript" -Mode Validate -IniPath "$ConfigIni"'
  Pop $ConfigResult
  ${If} $ConfigResult != 0
    MessageBox MB_ICONEXCLAMATION "Invalid configuration. Check application and logging settings. See installer details for more information."
    Abort
  ${EndIf}
FunctionEnd

Function .onInit
  ${IfNot} ${RunningX64}
    MessageBox MB_ICONSTOP "This installer requires 64-bit Windows."
    Abort
  ${EndIf}
  SetRegView 64
  SetShellVarContext all
FunctionEnd

Section "Install" SEC_MAIN
  SetShellVarContext all
  SetOutPath "$INSTDIR"
  File /r "..\publish\*.*"
  File "..\assets\Angular RSL Upload Icon.png"
  File "Register-StartupTask.ps1"
  File "Remove-StartupTask.ps1"
  ; Machine-wide settings and logs; selected values are written only after validation.
  CreateDirectory "$APPDATA\Room Setup Loader\Logs"
  ; Grant built-in Users Modify rights only on the Logs folder and its existing
  ; contents. SID S-1-5-32-545 works on localised Windows installations.
  ; This installer runs elevated; application users do not need elevation.
  nsExec::ExecToLog '"$SYSDIR\icacls.exe" "$APPDATA\Room Setup Loader\Logs" /grant "*S-1-5-32-545:(OI)(CI)M" /T /C'
  Pop $0
  ${If} $0 != 0
    MessageBox MB_ICONSTOP "Failed to configure log directory permissions (exit code $0). Check installer details."
    Abort
  ${EndIf}
  nsExec::ExecToLog '"$SYSDIR\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "$ConfigScript" -Mode Write -IniPath "$ConfigIni" -ConfigPath "$APPDATA\Room Setup Loader\config.json"'
  Pop $ConfigResult
  ${If} $ConfigResult != 0
    MessageBox MB_ICONSTOP "Failed to write configuration (exit code $ConfigResult). Check installer details."
    Abort
  ${EndIf}
  Delete "$INSTDIR\config.json"

  ; A system-level any-user logon task does not require users to be administrators.
  nsExec::ExecToLog '"$SYSDIR\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "$INSTDIR\Register-StartupTask.ps1" -ExePath "$INSTDIR\RoomSetupLoader.exe"'
  Pop $0
  ${If} $0 != 0
    MessageBox MB_ICONSTOP "Failed to register startup task (exit code $0). Check the installer details."
    Abort
  ${EndIf}

  ; All-users Start menu shortcuts. With SetShellVarContext all, $APPDATA
  ; resolves to ProgramData, where the application stores its shared config/logs.
  WriteUninstaller "$INSTDIR\Uninstall.exe"
  CreateDirectory "$SMPROGRAMS\Room Setup Loader"
  CreateShortcut "$SMPROGRAMS\Room Setup Loader\Room Setup Loader.lnk" "$INSTDIR\RoomSetupLoader.exe" "" "$INSTDIR\RoomSetupLoader.exe"
  ; Open the JSON file directly in Notepad, rather than its containing folder.
  CreateShortcut "$SMPROGRAMS\Room Setup Loader\Edit Configuration.lnk" "$WINDIR\notepad.exe" '"$APPDATA\Room Setup Loader\config.json"'
  CreateShortcut "$SMPROGRAMS\Room Setup Loader\Open Log Folder.lnk" "$WINDIR\explorer.exe" '"$APPDATA\Room Setup Loader\Logs"'
  CreateShortcut "$SMPROGRAMS\Room Setup Loader\Uninstall Room Setup Loader.lnk" "$INSTDIR\Uninstall.exe" "" "$INSTDIR\Uninstall.exe"
  WriteRegStr HKLM "${REGKEY}" "DisplayName" "${APPNAME}"
  WriteRegStr HKLM "${REGKEY}" "DisplayIcon" "$INSTDIR\RoomSetupLoader.exe"
  WriteRegStr HKLM "${REGKEY}" "DisplayVersion" "${APPVER}"
  WriteRegStr HKLM "${REGKEY}" "Publisher" "Internal Deployment"
  WriteRegStr HKLM "${REGKEY}" "UninstallString" '"$INSTDIR\Uninstall.exe"'
  WriteRegStr HKLM "${REGKEY}" "InstallLocation" "$INSTDIR"
  WriteRegDWORD HKLM "${REGKEY}" "NoModify" 1
  WriteRegDWORD HKLM "${REGKEY}" "NoRepair" 1
SectionEnd

Function un.RemoveDataPageCreate
  SetShellVarContext all
  StrCpy $un.RemoveAppData "0"
  nsDialogs::Create 1018
  Pop $0
  ${If} $0 == error
    Abort
  ${EndIf}
  ${NSD_CreateLabel} 0 0 100% 30u "Choose whether to keep application settings and logs for a future installation."
  Pop $0
  ${NSD_CreateCheckbox} 0 42u 100% 14u "Also remove application data from ProgramData"
  Pop $un.RemoveDataCheckbox
  ${NSD_Uncheck} $un.RemoveDataCheckbox
  ${NSD_CreateLabel} 0 65u 100% 25u "$APPDATA\Room Setup Loader"
  Pop $0
  ${NSD_CreateLabel} 0 93u 100% 35u "If selected, configuration, logs and other files in this folder will be permanently deleted. Leave unchecked to preserve them."
  Pop $0
  nsDialogs::Show
FunctionEnd

Function un.RemoveDataPageLeave
  SetShellVarContext all
  ${NSD_GetState} $un.RemoveDataCheckbox $un.RemoveAppData
  ${If} $un.RemoveAppData == ${BST_CHECKED}
    MessageBox MB_ICONEXCLAMATION|MB_YESNO|MB_DEFBUTTON2 "Permanently delete ALL files under:$\r$\n$\r$\n$APPDATA\Room Setup Loader$\r$\n$\r$\nThis includes configuration, logs and other application data. Continue?" IDYES +2
    Abort
  ${EndIf}
FunctionEnd

Section "Uninstall"
  SetShellVarContext all
  nsExec::ExecToLog '"$SYSDIR\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "$INSTDIR\Remove-StartupTask.ps1"'
  Pop $0
  ${If} $0 != 0
    MessageBox MB_ICONEXCLAMATION "Could not remove startup task (exit code $0). Check Task Scheduler."
  ${EndIf}
  RMDir /r "$INSTDIR"
  RMDir /r "$SMPROGRAMS\Room Setup Loader"
  DeleteRegKey HKLM "${REGKEY}"
  ; Only remove this application's machine-wide data when explicitly requested.
  ${If} $un.RemoveAppData == ${BST_CHECKED}
    StrCpy $1 "$APPDATA\Room Setup Loader"
    ; Never delete the ProgramData root or a missing/empty path.
    ${If} "$APPDATA" == ""
      DetailPrint "Skipping application-data deletion: ProgramData path is empty."
    ${ElseIf} ${FileExists} "$1\*.*"
      ClearErrors
      RMDir /r "$1"
      ; Verify the final directory state, not a potentially stale NSIS error flag.
      IfFileExists "$1\*.*" 0 +2
        MessageBox MB_ICONEXCLAMATION "Some application data could not be removed from $1. Close the application and inspect the folder manually."
    ${EndIf}
  ${EndIf}
SectionEnd
