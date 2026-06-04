; o-down Inno Setup installer script
; Compile with: ISCC.exe o-down.iss

#define AppName "o-down"
#define AppPublisher "o-down"
#define AppURL "https://github.com/anomalyco/o-down"
#define AppExe "o-down.exe"
#define AppVersion GetEnv("ODOWN_VERSION")
#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif

#define SourceDir GetEnv("ODOWN_SOURCE_DIR")
#ifndef SourceDir
  #define SourceDir "..\dist\publish\o-down"
#endif

#define OutputDir GetEnv("ODOWN_OUTPUT_DIR")
#ifndef OutputDir
  #define OutputDir "..\dist"
#endif

[Setup]
AppId={{B8F7A3D2-4E91-4F89-9C3F-1A2B3C4D5E6F}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
OutputDir={#OutputDir}
OutputBaseFilename={#AppName}-{#AppVersion}-setup
Compression=lzma2/max
SolidCompression=yes
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
UninstallDisplayIcon={app}\{#AppExe}
ChangesEnvironment=no
ChangesAssociations=no
AllowNoIcons=yes

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked
Name: "startmenuicon"; Description: "Create a &Start Menu shortcut"; GroupDescription: "Additional shortcuts:"

[Files]
; App + all dependencies + sidecars
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; Browser extension source files (for manual load in developer mode)
Source: "..\extensions\chrome\*"; DestDir: "{app}\extensions\chrome"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\extensions\firefox-mv2\*"; DestDir: "{app}\extensions\firefox-mv2"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\extensions\firefox-mv3\*"; DestDir: "{app}\extensions\firefox-mv3"; Flags: ignoreversion recursesubdirs createallsubdirs

[Dirs]
; Runtime data directory
Name: "{localappdata}\{#AppName}"; Permissions: users-modify
Name: "{localappdata}\{#AppName}\logs"; Permissions: users-modify
Name: "{localappdata}\{#AppName}\native-messaging\chrome"; Permissions: users-modify
Name: "{localappdata}\{#AppName}\native-messaging\firefox"; Permissions: users-modify

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"; Tasks: startmenuicon
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"; Tasks: desktopicon
Name: "{autoprograms}\{#AppName}\Uninstall {#AppName}"; Filename: "{uninstallexe}"

[Registry]
; Chrome native messaging
Root: HKCU; Subkey: "SOFTWARE\Google\Chrome\NativeMessagingHosts\o_down_native_messaging"; ValueType: string; ValueName: ""; ValueData: "{localappdata}\{#AppName}\native-messaging\chrome\o_down_native_messaging.json"; Flags: uninsdeletekey
; Edge native messaging
Root: HKCU; Subkey: "SOFTWARE\Microsoft\Edge\NativeMessagingHosts\o_down_native_messaging"; ValueType: string; ValueName: ""; ValueData: "{localappdata}\{#AppName}\native-messaging\chrome\o_down_native_messaging.json"; Flags: uninsdeletekey
; Firefox native messaging
Root: HKCU; Subkey: "SOFTWARE\Mozilla\NativeMessagingHosts\o_down_native_messaging"; ValueType: string; ValueName: ""; ValueData: "{localappdata}\{#AppName}\native-messaging\firefox\o_down_native_messaging.json"; Flags: uninsdeletekey

[Run]
Filename: "{app}\{#AppExe}"; Description: "Launch {#AppName}"; Flags: postinstall nowait skipifsilent shellexec

[UninstallRun]
; Clean up runtime data (optional)
Filename: "{cmd}"; Parameters: "/c rmdir /s /q ""{localappdata}\{#AppName}\native-messaging"""; Flags: runhidden

[Code]

const
  HostName = 'o_down_native_messaging';
  ChromeExtId = 'o-down-chrome-stub';
  FirefoxExtId = 'o-down-firefox-stub';
  DotNet8RuntimeUrl = 'https://dotnet.microsoft.com/download/dotnet/8.0';

function IsDotNet8DesktopRuntimeInstalled: Boolean;
var
  FindRec: TFindRec;
  Path: string;
begin
  Result := False;
  Path := ExpandConstant('{pf32}\dotnet\shared\Microsoft.WindowsDesktop.App');
  if DirExists(Path) and FindFirst(Path + '\*', FindRec) then
  begin
    try
      while True do
      begin
        if ((FindRec.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0) and
           (Length(FindRec.Name) >= 2) and
           (FindRec.Name[1] = '8') and (FindRec.Name[2] = '.') then
        begin
          Result := True;
          Break;
        end;
        if not FindNext(FindRec) then Break;
      end;
    finally
      FindClose(FindRec);
    end;
  end;
end;

function InitializeSetup: Boolean;
var
  ResultCode: Integer;
begin
  Result := True;
  if not IsDotNet8DesktopRuntimeInstalled then
  begin
    if MsgBox(
      'o-down''s browser extension host needs the .NET 8 Desktop Runtime, which was not found on this system.' + #13#10#13#10 +
      'The main app will still work, but the browser extension will not respond until the runtime is installed.' + #13#10#13#10 +
      'Open the .NET 8 download page now?',
      mbConfirmation, MB_YESNO) = idYes then
    begin
      ShellExecAsOriginalUser('open', DotNet8RuntimeUrl, '', '', SW_SHOWNORMAL, ewNoWait, ResultCode);
    end;
  end;
end;

procedure WriteNativeMessagingManifests;
var
  HostPath: string;
  ChromeDir, FirefoxDir: string;
  ChromeManifest, FirefoxManifest: string;
begin
  HostPath := ExpandConstant('{app}\o-down.NativeMessaging.exe');
  ChromeDir := ExpandConstant('{localappdata}\{#AppName}\native-messaging\chrome');
  FirefoxDir := ExpandConstant('{localappdata}\{#AppName}\native-messaging\firefox');

  ChromeManifest :=
    '{' + #13#10 +
    '  "name": "' + HostName + '",' + #13#10 +
    '  "description": "o-down native messaging host",' + #13#10 +
    '  "path": "' + HostPath + '",' + #13#10 +
    '  "type": "stdio",' + #13#10 +
    '  "allowed_origins": [' + #13#10 +
    '    "chrome-extension://' + ChromeExtId + '/"' + #13#10 +
    '  ]' + #13#10 +
    '}';

  FirefoxManifest :=
    '{' + #13#10 +
    '  "name": "' + HostName + '",' + #13#10 +
    '  "description": "o-down native messaging host",' + #13#10 +
    '  "path": "' + HostPath + '",' + #13#10 +
    '  "type": "stdio",' + #13#10 +
    '  "allowed_extensions": [' + #13#10 +
    '    "' + FirefoxExtId + '@temporary-addon",' + #13#10 +
    '    "' + ChromeExtId + '@temporary-addon"' + #13#10 +
    '  ]' + #13#10 +
    '}';

  SaveStringToFile(ChromeDir + '\' + HostName + '.json', ChromeManifest, False);
  SaveStringToFile(FirefoxDir + '\' + HostName + '.json', FirefoxManifest, False);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    WriteNativeMessagingManifests;
  end;
end;
