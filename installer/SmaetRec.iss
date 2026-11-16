; SMAET REC installer (Inno Setup 6). Built in CI: iscc /DAppVersion=1.0.0 /DSourceDir=..\publish\SmaetRec installer\SmaetRec.iss
#ifndef AppVersion
  #define AppVersion "1.0.3"
#endif
#ifndef SourceDir
  #define SourceDir "..\publish\SmaetRec"
#endif

[Setup]
AppId={{6E0B5C1E-7C0B-4C55-9A0E-5D7E0A51C0A1}
AppName=SMAET REC
AppVersion={#AppVersion}
AppPublisher=SMAET
DefaultDirName={autopf}\SMAET REC
DefaultGroupName=SMAET REC
DisableProgramGroupPage=yes
OutputDir=..\publish
OutputBaseFilename=SmaetRec-Setup-{#AppVersion}
SetupIconFile=..\src\SmaetRec.App\Assets\app.ico
UninstallDisplayIcon={app}\SmaetRec.exe
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
MinVersion=10.0.19041
WizardStyle=modern

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion

[Icons]
Name: "{group}\SMAET REC"; Filename: "{app}\SmaetRec.exe"
Name: "{autodesktop}\SMAET REC"; Filename: "{app}\SmaetRec.exe"; Tasks: desktopicon

[Run]
; Let the iPhone reach the app (AirPlay needs incoming connections and device discovery). The rule covers every
; network profile on purpose: Windows files the iPhone's USB/hotspot network under "Public", where a rule limited to
; private networks silently blocks the phone. The rule is limited to this program only.
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall add rule name=""SMAET REC"" dir=in action=allow program=""{app}\SmaetRec.exe"" enable=yes profile=any"; Flags: runhidden
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall add rule name=""SMAET REC mDNS"" dir=in action=allow program=""{app}\SmaetRec.exe"" protocol=UDP localport=5353 enable=yes profile=any"; Flags: runhidden
Filename: "{app}\SmaetRec.exe"; Description: "{cm:LaunchProgram,SMAET REC}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""SMAET REC"""; Flags: runhidden; RunOnceId: "RemoveFirewallRule"
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""SMAET REC mDNS"""; Flags: runhidden; RunOnceId: "RemoveMdnsFirewallRule"
