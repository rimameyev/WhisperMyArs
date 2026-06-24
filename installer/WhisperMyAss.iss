; Inno Setup script for WhisperMyAss
; Produces a single native WhisperMyAss-Setup.exe that:
;   1. Checks for the .NET 8 Desktop Runtime.
;   2. If missing, downloads it (with a progress page) and installs it silently.
;   3. Installs the small framework-dependent app and creates shortcuts.

#define AppName "WhisperMyAss"
#define AppVersion "0.1.0"
#define AppExe "WhisperMyAss.exe"
; Path to the published framework-dependent single-file exe.
#define SrcExe "..\publish\app\WhisperMyAss.exe"
; Official MS link — always resolves to the latest 8.0 Desktop Runtime (x64).
#define RuntimeUrl "https://aka.ms/dotnet/8.0/windowsdesktop-runtime-win-x64.exe"

[Setup]
AppId={{8B6C2F1A-9A2E-4C7D-9E4B-77D5C0A1F311}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=WhisperMyAss
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
UninstallDisplayName={#AppName}
OutputDir=..\publish
OutputBaseFilename={#AppName}-Setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"

[Files]
Source: "{#SrcExe}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
; Install the runtime only if it was found missing (see code below).
Filename: "{tmp}\windowsdesktop-runtime.exe"; Parameters: "/install /quiet /norestart"; \
  StatusMsg: "Installing the .NET 8 Desktop Runtime..."; Check: NeedsDotNet; Flags: waituntilterminated
; Offer to launch the app at the end.
Filename: "{app}\{#AppExe}"; Description: "Launch {#AppName} now"; \
  Flags: nowait postinstall skipifsilent

[Code]
var
  DotNetMissing: Boolean;
  DownloadPage: TDownloadWizardPage;

function IsDotNet8DesktopInstalled(): Boolean;
var
  FindRec: TFindRec;
  Base: String;
begin
  Result := False;
  Base := ExpandConstant('{commonpf64}\dotnet\shared\Microsoft.WindowsDesktop.App');
  if FindFirst(Base + '\8.*', FindRec) then
  begin
    try
      Result := True;   { at least one 8.x runtime folder exists }
    finally
      FindClose(FindRec);
    end;
  end;
end;

procedure InitializeWizard();
begin
  DotNetMissing := not IsDotNet8DesktopInstalled();
  DownloadPage := CreateDownloadPage(
    'Downloading prerequisites',
    'The Microsoft .NET 8 Desktop Runtime is required and will be downloaded.',
    nil);
end;

{ Used by the [Run] entry so the runtime installer only runs when needed. }
function NeedsDotNet(): Boolean;
begin
  Result := DotNetMissing;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if (CurPageID = wpReady) and DotNetMissing then
  begin
    DownloadPage.Clear;
    DownloadPage.Add('{#RuntimeUrl}', 'windowsdesktop-runtime.exe', '');
    DownloadPage.Show;
    try
      try
        DownloadPage.Download;
      except
        SuppressibleMsgBox(
          'Could not download the .NET 8 Desktop Runtime:' + #13#10 +
          AddPeriod(GetExceptionMessage) + #13#10 +
          'You can install it manually from https://dotnet.microsoft.com/download/dotnet/8.0',
          mbCriticalError, MB_OK, IDOK);
        Result := False;
      end;
    finally
      DownloadPage.Hide;
    end;
  end;
end;
