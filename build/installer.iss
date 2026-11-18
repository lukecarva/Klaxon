; Klaxon installer — Inno Setup. Bilingual (EN/PT-BR).
; Per-user install (no admin): lands in %LOCALAPPDATA%\Programs.
; Compile with: iscc build\installer.iss  (from the repository root)

#define AppFolder "Klaxon"
#define AppVersion "0.2.0"
#define AppPublisher "Lucas"
#define AppExe "Klaxon.exe"

[Setup]
; AppId identifies the product across versions — never change it once published,
; otherwise an update installs side by side instead of over the top.
AppId={{7B3C9E1A-4D52-4C8B-9E2F-1A6D5F0B7C34}
AppName={cm:AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
VersionInfoVersion={#AppVersion}
; Explicit because AppName uses constants (otherwise the compiler only warns).
VersionInfoProductName={#AppFolder}
VersionInfoDescription={#AppFolder} Setup

; No admin: installs for the current user only, in %LOCALAPPDATA%\Programs. The
; FOLDER name is fixed (brand), language-independent, to avoid creating two folders.
PrivilegesRequired=lowest
DefaultDirName={autopf}\{#AppFolder}
DisableProgramGroupPage=yes
DisableDirPage=auto

; Output
OutputDir=dist
OutputBaseFilename=Klaxon-Setup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes

; Appearance and identity
WizardStyle=modern
SetupIconFile=..\src\Klaxon.App\Assets\app.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={cm:AppName}

; Two languages: the wizard shows the language selector up front. English first =
; default. The choice also sets the app's initial language (see [Code]).
[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[CustomMessages]
english.AppName=Klaxon
brazilianportuguese.AppName=Klaxon
english.TaskDesktop=Create a desktop shortcut
brazilianportuguese.TaskDesktop=Criar um atalho na área de trabalho
english.TaskStartup=Start the alarm with Windows
brazilianportuguese.TaskStartup=Iniciar o despertador junto com o Windows
english.RunApp=Open Klaxon now
brazilianportuguese.RunApp=Abrir o Klaxon agora
english.RuntimeMissing=Klaxon needs the .NET 8 Desktop Runtime (x64), which was not found on this machine.%n%nDownload it from: https://dotnet.microsoft.com/download/dotnet/8.0/runtime (the "Desktop Runtime" option).%n%nContinue the installation anyway?
brazilianportuguese.RuntimeMissing=O Klaxon precisa do .NET 8 Desktop Runtime (x64), que não foi encontrado nesta máquina.%n%nBaixe em: https://dotnet.microsoft.com/download/dotnet/8.0/runtime (opção "Desktop Runtime").%n%nDeseja continuar a instalação mesmo assim?

[Tasks]
Name: "desktopicon"; Description: "{cm:TaskDesktop}"; Flags: unchecked
Name: "startup"; Description: "{cm:TaskStartup}"; Flags: unchecked

[Files]
; The publish is a single lean .exe that uses the machine's .NET 8 Desktop Runtime
; (its presence is checked in InitializeSetup, below).
Source: "publish\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{cm:AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{cm:AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; Optional autostart. Same name and format the app uses for its own "Start with
; Windows" button, so the two do not conflict — and it is removed on uninstall.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; \
    ValueType: string; ValueName: "Klaxon"; \
    ValueData: """{app}\{#AppExe}"" --minimized"; \
    Tasks: startup; Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:RunApp}"; \
    Flags: nowait postinstall skipifsilent

[UninstallRun]
; Close the app before removing, otherwise the .exe stays locked.
Filename: "{cmd}"; Parameters: "/C taskkill /IM {#AppExe} /F"; Flags: runhidden; RunOnceId: "CloseApp"

[UninstallDelete]
; Remove the product folder (the exe leaves on its own); user data in
; %APPDATA%\Klaxon is preserved on purpose.
Type: dirifempty; Name: "{app}"

[Code]
{ The build is framework-dependent: without the .NET 8 Desktop Runtime the app
  will not start. We check before installing and point to the download link, rather
  than letting the user hit a blunt error on a later double-click. }
function Net8DesktopInstalled(): Boolean;
var
  Base: String;
  Rec: TFindRec;
begin
  Result := False;
  Base := ExpandConstant('{commonpf64}\dotnet\shared\Microsoft.WindowsDesktop.App');
  if not DirExists(Base) then
    exit;

  if FindFirst(Base + '\8.*', Rec) then
  try
    repeat
      if (Rec.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0 then
      begin
        Result := True;
        break;
      end;
    until not FindNext(Rec);
  finally
    FindClose(Rec);
  end;
end;

function InitializeSetup(): Boolean;
begin
  Result := True;
  if Net8DesktopInstalled() then
    exit;

  { Ask instead of blocking: if detection fails in an edge case, the user can
    still proceed on their own. }
  Result := MsgBox(ExpandConstant('{cm:RuntimeMissing}'), mbConfirmation, MB_YESNO) = IDYES;
end;

{ Writes the wizard's language choice to settings.json, so the app opens in the
  same language. Today settings.json only stores the language, so rewriting it
  whole is safe. }
procedure CurStepChanged(CurStep: TSetupStep);
var
  Folder, FilePath, LangCode: String;
begin
  if CurStep <> ssPostInstall then
    exit;

  if ActiveLanguage() = 'brazilianportuguese' then
    LangCode := 'pt-BR'
  else
    LangCode := 'en';

  Folder := ExpandConstant('{userappdata}\Klaxon');
  ForceDirectories(Folder);
  FilePath := Folder + '\settings.json';
  SaveStringToFile(FilePath, '{' + #13#10 + '  "Language": "' + LangCode + '"' + #13#10 + '}' + #13#10, False);
end;
