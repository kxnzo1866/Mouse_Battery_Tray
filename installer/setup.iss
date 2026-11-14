; Mouse Battery Tray installer (Inno Setup 6 - https://jrsoftware.org/isinfo.php)
; Build: .\build.ps1 -Setup   (or open this file in Inno Setup and click Compile)
; The version number must also be updated at the top of src\MouseBatteryTray.cs.
; Silent install for testing: /VERYSILENT /LANG=french|english /DIR="..." and /PORTABLE=1 for the portable mode.

#define AppName "Mouse Battery Tray"
#define AppVersion "1.3.0"
#define AppExe "MouseBatteryTray.exe"
#define RunValue "MouseBatteryTray"
; Versions up to 1.0.0 were named "Pulsar Battery Tray": they are uninstalled automatically.
#define OldAppId "{6E0B5C3A-7B0D-4E53-9F0C-2B6C1A4D8E71}"
#define OldExe "PulsarBattery.exe"

[Setup]
AppId={{F3477553-85CB-47BC-8BBF-E7B255F3805A}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
DefaultDirName={autopf}\{#AppName}
; No administrator rights: everything is installed in the user's profile.
PrivilegesRequired=lowest
DisableProgramGroupPage=yes
DisableDirPage=no
UsePreviousAppDir=no
; Portable mode: no uninstaller and no entry in "Installed apps".
Uninstallable=not IsPortable
CreateUninstallRegKey=not IsPortable
UninstallDisplayIcon={app}\{#AppExe}
SetupIconFile=..\app.ico
LicenseFile=..\LICENSE
OutputDir=..\dist
OutputBaseFilename=MouseBatteryTray-Setup-{#AppVersion}
WizardStyle=modern
Compression=lzma2
SolidCompression=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "french"; MessagesFile: "compiler:Languages\French.isl"

[CustomMessages]
french.ModeTitle=Type d'installation
french.ModeSubtitle=Comment voulez-vous installer {#AppName} ?
french.ModeText=Choisissez un type d'installation, puis cliquez sur Suivant.
french.ModeNormal=Installation standard (recommandée)
french.ModePortable=Version portable
french.StartupTask=Lancer {#AppName} au démarrage de Windows
french.LaunchNow=Lancer {#AppName} maintenant
english.ModeTitle=Installation type
english.ModeSubtitle=How do you want to install {#AppName}?
english.ModeText=Choose an installation type, then click Next.
english.ModeNormal=Standard installation (recommended)
english.ModePortable=Portable version
english.StartupTask=Start {#AppName} when Windows starts
english.LaunchNow=Start {#AppName} now

[Tasks]
; Offered for the standard installation only. In portable mode the option remains available
; from the tray menu (right-click the icon) for those who still want it.
Name: "startup"; Description: "{cm:StartupTask}"; Check: not IsPortable

[Files]
Source: "..\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"; Check: not IsPortable

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "{#RunValue}"; ValueData: """{app}\{#AppExe}"""; Tasks: startup; Flags: uninsdeletevalue
; App settings (language, theme, alert, ...): removed on uninstall.
Root: HKCU; Subkey: "Software\MouseBatteryTray"; Flags: uninsdeletekey dontcreatekey; Check: not IsPortable
; Language of the app on first launch = language chosen in the installer (kept if the user already changed it).
Root: HKCU; Subkey: "Software\MouseBatteryTray"; ValueType: string; ValueName: "Language"; ValueData: "{code:AppLanguage}"; Flags: createvalueifdoesntexist uninsdeletekey; Check: not IsPortable

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchNow}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Closes the app before deleting its files.
Filename: "{sys}\taskkill.exe"; Parameters: "/IM {#AppExe} /F"; Flags: runhidden; RunOnceId: "KillApp"

[UninstallDelete]
Type: files; Name: "{app}\portable.ini"

[Code]
var
  ModePage: TInputOptionWizardPage;

function IsPortable: Boolean;
begin
  Result := (ModePage <> nil) and (ModePage.SelectedValueIndex = 1);
end;

// Language code read by the app: "fr" or "en".
function AppLanguage(Param: String): String;
begin
  if ActiveLanguage = 'french' then
    Result := 'fr'
  else
    Result := 'en';
end;

procedure InitializeWizard;
begin
  ModePage := CreateInputOptionPage(wpLicense,
    CustomMessage('ModeTitle'), CustomMessage('ModeSubtitle'), CustomMessage('ModeText'), True, False);
  ModePage.Add(CustomMessage('ModeNormal'));
  ModePage.Add(CustomMessage('ModePortable'));
  ModePage.SelectedValueIndex := 0;
  if ExpandConstant('{param:PORTABLE|0}') = '1' then
    ModePage.SelectedValueIndex := 1;
end;

// Suggests a folder suited to the chosen type (unless a folder was given with /DIR=).
function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if (CurPageID = ModePage.ID) and (ExpandConstant('{param:DIR|}') = '') then
  begin
    if IsPortable then
      WizardForm.DirEdit.Text := ExpandConstant('{userdesktop}\MouseBatteryTray')
    else
      WizardForm.DirEdit.Text := ExpandConstant('{autopf}\{#AppName}');
  end;
end;

// Closes a running copy so the exe can be replaced. A standard installation also closes and
// silently uninstalls a previous "Pulsar Battery Tray" (a portable copy leaves installed versions alone).
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Code: Integer;
  OldUninstaller: String;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/IM {#AppExe} /F', '', SW_HIDE, ewWaitUntilTerminated, Code);
  if not IsPortable then
  begin
    Exec(ExpandConstant('{sys}\taskkill.exe'), '/IM {#OldExe} /F', '', SW_HIDE, ewWaitUntilTerminated, Code);
    if RegQueryStringValue(HKEY_CURRENT_USER,
         'Software\Microsoft\Windows\CurrentVersion\Uninstall\{#OldAppId}_is1', 'UninstallString', OldUninstaller) then
      Exec(RemoveQuotes(OldUninstaller), '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART', '', SW_HIDE, ewWaitUntilTerminated, Code);
  end;
  Result := '';
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Marker: String;
begin
  if CurStep = ssPostInstall then
  begin
    Marker := ExpandConstant('{app}\portable.ini');
    if IsPortable then
    begin
      // The presence of this file switches the app to portable mode. It also holds all the settings,
      // starting with the language chosen in the installer.
      if not FileExists(Marker) then
        SaveStringToFile(Marker, '; Mouse Battery Tray - portable mode' + #13#10 +
          'Language=' + AppLanguage('') + #13#10 + 'LowBatteryAlert=1' + #13#10, False);
    end
    else
    begin
      DeleteFile(Marker);
      // Box unticked on a reinstall: remove the previous start-with-Windows entry.
      if not WizardIsTaskSelected('startup') then
        RegDeleteValue(HKEY_CURRENT_USER, 'Software\Microsoft\Windows\CurrentVersion\Run', '{#RunValue}');
    end;
  end;
end;
