; Kalendra installer (NSIS). Build: makensis -DVERSION=1.0.0 -DSRC=<publish dir> kalendra.nsi
Unicode true
!include "MUI2.nsh"
!include "FileFunc.nsh"

!ifndef VERSION
  !define VERSION "1.0.0"
!endif
!ifndef SRC
  !define SRC "../pub"
!endif
!define APP "Kalendra"
!define UNINST_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\Kalendra"

Name "${APP}"
OutFile "Kalendra-Setup-${VERSION}.exe"
; per-user install: no administrator rights needed
RequestExecutionLevel user
InstallDir "$LOCALAPPDATA\Programs\${APP}"
InstallDirRegKey HKCU "Software\${APP}" "InstallDir"
SetCompressor /SOLID lzma
BrandingText "${APP} ${VERSION}"

VIProductVersion "${VERSION}.0"
VIAddVersionKey /LANG=1031 "ProductName" "${APP}"
VIAddVersionKey /LANG=1031 "FileDescription" "${APP} Setup"
VIAddVersionKey /LANG=1031 "FileVersion" "${VERSION}"
VIAddVersionKey /LANG=1031 "ProductVersion" "${VERSION}"
VIAddVersionKey /LANG=1031 "LegalCopyright" " "

!define MUI_ICON "../src/Kalendra/Assets/kalendra.ico"
!define MUI_UNICON "../src/Kalendra/Assets/kalendra.ico"
!define MUI_ABORTWARNING
!define MUI_WELCOMEPAGE_TEXT "Dieser Assistent installiert ${APP} ${VERSION} auf deinem PC.$\r$\n$\r$\nAdministratorrechte sind nicht nötig. Deine Kalender und Anmeldungen aus einer früheren Version bleiben erhalten.$\r$\n$\r$\nBitte schließe ${APP}, falls es gerade läuft."
!define MUI_FINISHPAGE_RUN "$INSTDIR\Kalendra.exe"
!define MUI_FINISHPAGE_RUN_TEXT "${APP} jetzt starten"

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_COMPONENTS
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_COMPONENTS
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "German"

Section "${APP}" SecMain
  SectionIn RO
  SetOutPath "$INSTDIR"
  ; a running Kalendra would block the update
  nsExec::Exec 'taskkill /IM Kalendra.exe'
  Sleep 800
  File "${SRC}\Kalendra.exe"
  WriteUninstaller "$INSTDIR\Deinstallieren.exe"
  CreateShortCut "$SMPROGRAMS\${APP}.lnk" "$INSTDIR\Kalendra.exe"
  WriteRegStr HKCU "Software\${APP}" "InstallDir" "$INSTDIR"
  WriteRegStr HKCU "${UNINST_KEY}" "DisplayName" "${APP}"
  WriteRegStr HKCU "${UNINST_KEY}" "DisplayVersion" "${VERSION}"
  WriteRegStr HKCU "${UNINST_KEY}" "Publisher" "${APP}"
  WriteRegStr HKCU "${UNINST_KEY}" "DisplayIcon" "$INSTDIR\Kalendra.exe"
  WriteRegStr HKCU "${UNINST_KEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "${UNINST_KEY}" "UninstallString" '"$INSTDIR\Deinstallieren.exe"'
  WriteRegDWORD HKCU "${UNINST_KEY}" "NoModify" 1
  WriteRegDWORD HKCU "${UNINST_KEY}" "NoRepair" 1
  ${GetSize} "$INSTDIR" "/S=0K" $0 $1 $2
  WriteRegDWORD HKCU "${UNINST_KEY}" "EstimatedSize" $0
SectionEnd

Section "Verknüpfung auf dem Desktop" SecDesktop
  CreateShortCut "$DESKTOP\${APP}.lnk" "$INSTDIR\Kalendra.exe"
SectionEnd

Section /o "Beim Windows-Start öffnen" SecAutostart
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "${APP}" '"$INSTDIR\Kalendra.exe"'
SectionEnd

!insertmacro MUI_FUNCTION_DESCRIPTION_BEGIN
  !insertmacro MUI_DESCRIPTION_TEXT ${SecMain} "Das Programm und ein Eintrag im Startmenü."
  !insertmacro MUI_DESCRIPTION_TEXT ${SecDesktop} "Symbol auf dem Desktop."
  !insertmacro MUI_DESCRIPTION_TEXT ${SecAutostart} "Kalendra startet mit Windows und hält die Kalender aktuell."
!insertmacro MUI_FUNCTION_DESCRIPTION_END

Section "un.${APP}" UnMain
  SectionIn RO
  nsExec::Exec 'taskkill /IM Kalendra.exe'
  Sleep 800
  Delete "$INSTDIR\Kalendra.exe"
  Delete "$INSTDIR\Deinstallieren.exe"
  RMDir "$INSTDIR"
  Delete "$SMPROGRAMS\${APP}.lnk"
  Delete "$DESKTOP\${APP}.lnk"
  DeleteRegValue HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "${APP}"
  DeleteRegKey HKCU "${UNINST_KEY}"
  DeleteRegKey HKCU "Software\${APP}"
SectionEnd

Section /o "un.Auch meine Kalenderdaten auf diesem PC löschen" UnData
  RMDir /r "$APPDATA\${APP}"
SectionEnd

!insertmacro MUI_UNFUNCTION_DESCRIPTION_BEGIN
  !insertmacro MUI_DESCRIPTION_TEXT ${UnMain} "Programm, Verknüpfungen und Autostart entfernen."
  !insertmacro MUI_DESCRIPTION_TEXT ${UnData} "Löscht die lokale Kopie der Termine und die gespeicherten Anmeldungen. In iCloud und Google bleibt alles erhalten."
!insertmacro MUI_UNFUNCTION_DESCRIPTION_END
