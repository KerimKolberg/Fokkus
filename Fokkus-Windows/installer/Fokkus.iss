; Fokkus installer (Inno Setup 6). Build with ..\build-installer.cmd
; Installs per user (no admin rights needed): %LOCALAPPDATA%\Programs\Fokkus,
; Start menu entry, desktop shortcut, entry in Settings > Apps for uninstalling.

#define AppName "Fokkus"
#define AppVersion "1.0.0"
#define AppExe "Fokkus.exe"
#define AppUrl "https://github.com/KerimKolberg/Fokkus"

[Setup]
AppId={{8F3C2E61-4B7A-4D6B-9C1E-F0A7B5D2C9E4}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=KerimKolberg
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}
AppUpdatesURL={#AppUrl}/releases
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputDir=..\..\dist
OutputBaseFilename=Fokkus-Setup-{#AppVersion}
SetupIconFile=..\assets\fokkus.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
WizardStyle=modern
WizardImageFile=wizard-large.bmp,wizard-large-2x.bmp
WizardSmallImageFile=wizard-small.bmp,wizard-small-2x.bmp
Compression=lzma2/max
SolidCompression=yes
; The running app holds this mutex: setup/uninstall ask you to close Fokkus first.
AppMutex=Fokkus-single-instance-5c1e
CloseApplications=yes
RestartApplications=no
MinVersion=10.0
VersionInfoVersion={#AppVersion}
VersionInfoProductName={#AppName}
VersionInfoDescription={#AppName} Setup

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "german"; MessagesFile: "compiler:Languages\German.isl"

[CustomMessages]
english.DeleteData=Also delete your Fokkus settings, study plan and focus statistics?%n%n(Choose No to keep them for a later reinstall.)
german.DeleteData=Auch deine Fokkus-Einstellungen, deinen Lernplan und deine Fokus-Statistiken löschen?%n%n(Wähle Nein, um sie für eine spätere Neuinstallation zu behalten.)

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "..\bin\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"; Comment: "Pomodoro study timer that locks in your focus"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon; Comment: "Pomodoro study timer that locks in your focus"

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Make sure the Windows proxy setting is back to normal, even if Fokkus was ever killed mid-focus.
Filename: "{app}\{#AppExe}"; Parameters: "--restore-internet --silent"; Flags: runhidden waituntilterminated; RunOnceId: "RestoreInternet"

[Registry]
; Created by the app itself when you turn on "Start with Windows" (or as a crash safety net) - removed on uninstall.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueName: "Fokkus"; ValueType: none; Flags: uninsdeletevalue dontcreatekey
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\RunOnce"; ValueName: "Fokkus-restore"; ValueType: none; Flags: uninsdeletevalue dontcreatekey

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if (CurUninstallStep = usPostUninstall) and (not UninstallSilent) then
    if MsgBox(CustomMessage('DeleteData'), mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
      DelTree(ExpandConstant('{userappdata}\Fokkus'), True, True, True);
end;
