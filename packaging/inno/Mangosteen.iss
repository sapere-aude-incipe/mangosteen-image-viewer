#ifndef AppVersion
#define AppVersion "0.1.0"
#endif

#ifndef SourceDir
#define SourceDir "..\..\publish\installer-input"
#endif

#ifndef OutputDir
#define OutputDir "..\..\dist"
#endif

#ifndef OptionalComponentsDir
#define OptionalComponentsDir "..\..\publish\optional-components"
#endif

#ifndef IncludeOptionalComponents
#define IncludeOptionalComponents 0
#endif

#define AppIconFile "..\..\src\Mangosteen\Assets\mangosteen.ico"
#define AppShortName "Mangosteen"
#define AppDisplayName "Mangosteen Image Viewer"
#define AppPublisher "sapere-aude-incipe"
#define AppExeName "Mangosteen.exe"
#define AppId "{{5505BFA7-AFF8-4C6E-8B60-52EDF84880D3}"
#define AppImageProgId "Mangosteen.Image"
#define AppModelProgId "Mangosteen.Model"

[Setup]
AppId={#AppId}
AppName={#AppDisplayName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL=https://github.com/sapere-aude-incipe/mangosteen-image-viewer
AppSupportURL=https://github.com/sapere-aude-incipe/mangosteen-image-viewer/issues
AppUpdatesURL=https://github.com/sapere-aude-incipe/mangosteen-image-viewer/releases
DefaultDirName={localappdata}\Programs\{#AppShortName}
DefaultGroupName={#AppDisplayName}
DisableProgramGroupPage=yes
OutputDir={#OutputDir}
OutputBaseFilename={#AppShortName}-Setup-{#AppVersion}-x64
SetupIconFile={#AppIconFile}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
UninstallDisplayName={#AppDisplayName}
UninstallDisplayIcon={app}\{#AppExeName}
ChangesAssociations=yes
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "norwegian"; MessagesFile: "compiler:Languages\Norwegian.isl"
Name: "german"; MessagesFile: "compiler:Languages\German.isl"
Name: "french"; MessagesFile: "compiler:Languages\French.isl"
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"
Name: "polish"; MessagesFile: "compiler:Languages\Polish.isl"
Name: "turkish"; MessagesFile: "compiler:Languages\Turkish.isl"
Name: "japanese"; MessagesFile: "compiler:Languages\Japanese.isl"
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Tasks]
Name: "associatefiles"; Description: "Register supported image file types with {#AppDisplayName}"; GroupDescription: "File associations:"
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Types]
; The first type is the default: a lean core install unless Full or Custom is chosen.
Name: "core"; Description: "Core image viewer"
#if IncludeOptionalComponents
Name: "full"; Description: "Full: core viewer with 3D model viewing"
#endif

[Components]
#if IncludeOptionalComponents
Name: "core"; Description: "Mangosteen Image Viewer"; Types: core full; Flags: fixed
Name: "modelviewer"; Description: "3D model viewing (F3D 3.5.0, adds about 119 MB)"; Types: full
#else
Name: "core"; Description: "Mangosteen Image Viewer"; Types: core; Flags: fixed
#endif

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Components: core
#if IncludeOptionalComponents
Source: "{#OptionalComponentsDir}\model-viewer\*"; DestDir: "{app}\components\model-viewer"; Flags: ignoreversion recursesubdirs createallsubdirs; Components: modelviewer
#endif

[InstallDelete]
; GPU large-image rendering became a built-in setting; remove the old switch-only component.
Type: filesandordirs; Name: "{app}\components\gpu-large-images"

[Icons]
Name: "{autoprograms}\{#AppDisplayName}"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\{#AppDisplayName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Registry]
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}"; Flags: deletekey
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}"; ValueType: string; ValueName: "FriendlyAppName"; ValueData: "{#AppDisplayName}"; Flags: uninsdeletekey; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExeName}"",0"; Flags: uninsdeletekey; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExeName}"" ""%1"""; Flags: uninsdeletekey; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\Capabilities"; ValueType: string; ValueName: "ApplicationName"; ValueData: "{#AppDisplayName}"; Flags: uninsdeletekey; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\Capabilities"; ValueType: string; ValueName: "ApplicationDescription"; ValueData: "Simple, fast, privacy-respecting image viewer"; Flags: uninsdeletekey; Tasks: associatefiles
#if IncludeOptionalComponents
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".stl"; ValueData: "{#AppModelProgId}"; Flags: uninsdeletekey; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".ply"; ValueData: "{#AppModelProgId}"; Flags: uninsdeletekey; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".obj"; ValueData: "{#AppModelProgId}"; Flags: uninsdeletekey; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".gltf"; ValueData: "{#AppModelProgId}"; Flags: uninsdeletekey; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".glb"; ValueData: "{#AppModelProgId}"; Flags: uninsdeletekey; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".3ds"; ValueData: "{#AppModelProgId}"; Flags: uninsdeletekey; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".wrl"; ValueData: "{#AppModelProgId}"; Flags: uninsdeletekey; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".vrml"; ValueData: "{#AppModelProgId}"; Flags: uninsdeletekey; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".mdl"; ValueData: "{#AppModelProgId}"; Flags: uninsdeletekey; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".fbx"; ValueData: "{#AppModelProgId}"; Flags: uninsdeletekey; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".dae"; ValueData: "{#AppModelProgId}"; Flags: uninsdeletekey; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".dxf"; ValueData: "{#AppModelProgId}"; Flags: uninsdeletekey; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".off"; ValueData: "{#AppModelProgId}"; Flags: uninsdeletekey; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".x"; ValueData: "{#AppModelProgId}"; Flags: uninsdeletekey; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".3mf"; ValueData: "{#AppModelProgId}"; Flags: uninsdeletekey; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".drc"; ValueData: "{#AppModelProgId}"; Flags: uninsdeletekey; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".abc"; ValueData: "{#AppModelProgId}"; Flags: uninsdeletekey; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".usd"; ValueData: "{#AppModelProgId}"; Flags: uninsdeletekey; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".usda"; ValueData: "{#AppModelProgId}"; Flags: uninsdeletekey; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".usdc"; ValueData: "{#AppModelProgId}"; Flags: uninsdeletekey; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".usdz"; ValueData: "{#AppModelProgId}"; Flags: uninsdeletekey; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".step"; ValueData: "{#AppModelProgId}"; Flags: uninsdeletekey; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".stp"; ValueData: "{#AppModelProgId}"; Flags: uninsdeletekey; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".stpnc"; ValueData: "{#AppModelProgId}"; Flags: uninsdeletekey; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".p21"; ValueData: "{#AppModelProgId}"; Flags: uninsdeletekey; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".iges"; ValueData: "{#AppModelProgId}"; Flags: uninsdeletekey; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".igs"; ValueData: "{#AppModelProgId}"; Flags: uninsdeletekey; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".brep"; ValueData: "{#AppModelProgId}"; Flags: uninsdeletekey; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".xbf"; ValueData: "{#AppModelProgId}"; Flags: uninsdeletekey; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".ifc"; ValueData: "{#AppModelProgId}"; Flags: uninsdeletekey; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".vtk"; ValueData: "{#AppModelProgId}"; Flags: uninsdeletekey; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".vtp"; ValueData: "{#AppModelProgId}"; Flags: uninsdeletekey; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".vtu"; ValueData: "{#AppModelProgId}"; Flags: uninsdeletekey; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".vtr"; ValueData: "{#AppModelProgId}"; Flags: uninsdeletekey; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".vts"; ValueData: "{#AppModelProgId}"; Flags: uninsdeletekey; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".vtm"; ValueData: "{#AppModelProgId}"; Flags: uninsdeletekey; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".vtkhdf"; ValueData: "{#AppModelProgId}"; Flags: uninsdeletekey; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".exo"; ValueData: "{#AppModelProgId}"; Flags: uninsdeletekey; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".ex2"; ValueData: "{#AppModelProgId}"; Flags: uninsdeletekey; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".ncdf"; ValueData: "{#AppModelProgId}"; Flags: uninsdeletekey; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".vti"; ValueData: "{#AppModelProgId}"; Flags: uninsdeletekey; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".mha"; ValueData: "{#AppModelProgId}"; Flags: uninsdeletekey; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".mhd"; ValueData: "{#AppModelProgId}"; Flags: uninsdeletekey; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".nrrd"; ValueData: "{#AppModelProgId}"; Flags: uninsdeletekey; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".nhdr"; ValueData: "{#AppModelProgId}"; Flags: uninsdeletekey; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".dcm"; ValueData: "{#AppModelProgId}"; Flags: uninsdeletekey; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".vdb"; ValueData: "{#AppModelProgId}"; Flags: uninsdeletekey; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".pts"; ValueData: "{#AppModelProgId}"; Flags: uninsdeletekey; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".las"; ValueData: "{#AppModelProgId}"; Flags: uninsdeletekey; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".laz"; ValueData: "{#AppModelProgId}"; Flags: uninsdeletekey; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".pcd"; ValueData: "{#AppModelProgId}"; Flags: uninsdeletekey; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".bpf"; ValueData: "{#AppModelProgId}"; Flags: uninsdeletekey; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".slpk"; ValueData: "{#AppModelProgId}"; Flags: uninsdeletekey; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".splat"; ValueData: "{#AppModelProgId}"; Flags: uninsdeletekey; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".spz"; ValueData: "{#AppModelProgId}"; Flags: uninsdeletekey; Tasks: associatefiles; Components: modelviewer
#endif
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".3fr"; ValueData: "{#AppImageProgId}"; Flags: uninsdeletekey; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".ari"; ValueData: "{#AppImageProgId}"; Flags: uninsdeletekey; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".arw"; ValueData: "{#AppImageProgId}"; Flags: uninsdeletekey; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".avif"; ValueData: "{#AppImageProgId}"; Flags: uninsdeletekey; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".bay"; ValueData: "{#AppImageProgId}"; Flags: uninsdeletekey; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".jpg"; ValueData: "{#AppImageProgId}"; Flags: uninsdeletekey; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".bmp"; ValueData: "{#AppImageProgId}"; Flags: uninsdeletekey; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".cap"; ValueData: "{#AppImageProgId}"; Flags: uninsdeletekey; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".cr2"; ValueData: "{#AppImageProgId}"; Flags: uninsdeletekey; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".cr3"; ValueData: "{#AppImageProgId}"; Flags: uninsdeletekey; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".crw"; ValueData: "{#AppImageProgId}"; Flags: uninsdeletekey; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".cur"; ValueData: "{#AppImageProgId}"; Flags: uninsdeletekey; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".dcr"; ValueData: "{#AppImageProgId}"; Flags: uninsdeletekey; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".dcs"; ValueData: "{#AppImageProgId}"; Flags: uninsdeletekey; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".dds"; ValueData: "{#AppImageProgId}"; Flags: uninsdeletekey; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".dib"; ValueData: "{#AppImageProgId}"; Flags: uninsdeletekey; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".dng"; ValueData: "{#AppImageProgId}"; Flags: uninsdeletekey; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".drf"; ValueData: "{#AppImageProgId}"; Flags: uninsdeletekey; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".erf"; ValueData: "{#AppImageProgId}"; Flags: uninsdeletekey; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".exif"; ValueData: "{#AppImageProgId}"; Flags: uninsdeletekey; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".exr"; ValueData: "{#AppImageProgId}"; Flags: uninsdeletekey; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".fff"; ValueData: "{#AppImageProgId}"; Flags: uninsdeletekey; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".gif"; ValueData: "{#AppImageProgId}"; Flags: uninsdeletekey; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".gpr"; ValueData: "{#AppImageProgId}"; Flags: uninsdeletekey; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".hdr"; ValueData: "{#AppImageProgId}"; Flags: uninsdeletekey; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".heic"; ValueData: "{#AppImageProgId}"; Flags: uninsdeletekey; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".heif"; ValueData: "{#AppImageProgId}"; Flags: uninsdeletekey; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".ico"; ValueData: "{#AppImageProgId}"; Flags: uninsdeletekey; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".iiq"; ValueData: "{#AppImageProgId}"; Flags: uninsdeletekey; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".jfif"; ValueData: "{#AppImageProgId}"; Flags: uninsdeletekey; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".jpe"; ValueData: "{#AppImageProgId}"; Flags: uninsdeletekey; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".jpeg"; ValueData: "{#AppImageProgId}"; Flags: uninsdeletekey; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".jxl"; ValueData: "{#AppImageProgId}"; Flags: uninsdeletekey; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".k25"; ValueData: "{#AppImageProgId}"; Flags: uninsdeletekey; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".kdc"; ValueData: "{#AppImageProgId}"; Flags: uninsdeletekey; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".mdc"; ValueData: "{#AppImageProgId}"; Flags: uninsdeletekey; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".mef"; ValueData: "{#AppImageProgId}"; Flags: uninsdeletekey; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".mos"; ValueData: "{#AppImageProgId}"; Flags: uninsdeletekey; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".mrw"; ValueData: "{#AppImageProgId}"; Flags: uninsdeletekey; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".nef"; ValueData: "{#AppImageProgId}"; Flags: uninsdeletekey; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".nrw"; ValueData: "{#AppImageProgId}"; Flags: uninsdeletekey; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".orf"; ValueData: "{#AppImageProgId}"; Flags: uninsdeletekey; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".pbm"; ValueData: "{#AppImageProgId}"; Flags: uninsdeletekey; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".pef"; ValueData: "{#AppImageProgId}"; Flags: uninsdeletekey; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".pcx"; ValueData: "{#AppImageProgId}"; Flags: uninsdeletekey; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".pgm"; ValueData: "{#AppImageProgId}"; Flags: uninsdeletekey; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".png"; ValueData: "{#AppImageProgId}"; Flags: uninsdeletekey; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".ppm"; ValueData: "{#AppImageProgId}"; Flags: uninsdeletekey; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".psb"; ValueData: "{#AppImageProgId}"; Flags: uninsdeletekey; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".psd"; ValueData: "{#AppImageProgId}"; Flags: uninsdeletekey; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".ptx"; ValueData: "{#AppImageProgId}"; Flags: uninsdeletekey; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".pxn"; ValueData: "{#AppImageProgId}"; Flags: uninsdeletekey; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".qoi"; ValueData: "{#AppImageProgId}"; Flags: uninsdeletekey; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".raf"; ValueData: "{#AppImageProgId}"; Flags: uninsdeletekey; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".raw"; ValueData: "{#AppImageProgId}"; Flags: uninsdeletekey; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".rw2"; ValueData: "{#AppImageProgId}"; Flags: uninsdeletekey; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".rwl"; ValueData: "{#AppImageProgId}"; Flags: uninsdeletekey; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".sr2"; ValueData: "{#AppImageProgId}"; Flags: uninsdeletekey; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".srf"; ValueData: "{#AppImageProgId}"; Flags: uninsdeletekey; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".srw"; ValueData: "{#AppImageProgId}"; Flags: uninsdeletekey; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".svg"; ValueData: "{#AppImageProgId}"; Flags: uninsdeletekey; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".svgz"; ValueData: "{#AppImageProgId}"; Flags: uninsdeletekey; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".tga"; ValueData: "{#AppImageProgId}"; Flags: uninsdeletekey; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".tif"; ValueData: "{#AppImageProgId}"; Flags: uninsdeletekey; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".tiff"; ValueData: "{#AppImageProgId}"; Flags: uninsdeletekey; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".webp"; ValueData: "{#AppImageProgId}"; Flags: uninsdeletekey; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".wmf"; ValueData: "{#AppImageProgId}"; Flags: uninsdeletekey; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".x3f"; ValueData: "{#AppImageProgId}"; Flags: uninsdeletekey; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".xbm"; ValueData: "{#AppImageProgId}"; Flags: uninsdeletekey; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".xpm"; ValueData: "{#AppImageProgId}"; Flags: uninsdeletekey; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\{#AppImageProgId}"; ValueType: string; ValueName: ""; ValueData: "{#AppDisplayName} image"; Flags: uninsdeletekey; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\{#AppImageProgId}\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExeName}"",0"; Flags: uninsdeletekey; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\{#AppImageProgId}\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExeName}"" ""%1"""; Flags: uninsdeletekey; Tasks: associatefiles
#if IncludeOptionalComponents
Root: HKA; Subkey: "Software\Classes\{#AppModelProgId}"; ValueType: string; ValueName: ""; ValueData: "{#AppDisplayName} 3D model"; Flags: uninsdeletekey; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\{#AppModelProgId}\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExeName}"",0"; Flags: uninsdeletekey; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\{#AppModelProgId}\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExeName}"" ""%1"""; Flags: uninsdeletekey; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\.stl\OpenWithProgids"; ValueType: string; ValueName: "{#AppModelProgId}"; ValueData: ""; Flags: uninsdeletevalue; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\.ply\OpenWithProgids"; ValueType: string; ValueName: "{#AppModelProgId}"; ValueData: ""; Flags: uninsdeletevalue; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\.obj\OpenWithProgids"; ValueType: string; ValueName: "{#AppModelProgId}"; ValueData: ""; Flags: uninsdeletevalue; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\.gltf\OpenWithProgids"; ValueType: string; ValueName: "{#AppModelProgId}"; ValueData: ""; Flags: uninsdeletevalue; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\.glb\OpenWithProgids"; ValueType: string; ValueName: "{#AppModelProgId}"; ValueData: ""; Flags: uninsdeletevalue; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\.3ds\OpenWithProgids"; ValueType: string; ValueName: "{#AppModelProgId}"; ValueData: ""; Flags: uninsdeletevalue; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\.wrl\OpenWithProgids"; ValueType: string; ValueName: "{#AppModelProgId}"; ValueData: ""; Flags: uninsdeletevalue; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\.vrml\OpenWithProgids"; ValueType: string; ValueName: "{#AppModelProgId}"; ValueData: ""; Flags: uninsdeletevalue; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\.mdl\OpenWithProgids"; ValueType: string; ValueName: "{#AppModelProgId}"; ValueData: ""; Flags: uninsdeletevalue; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\.fbx\OpenWithProgids"; ValueType: string; ValueName: "{#AppModelProgId}"; ValueData: ""; Flags: uninsdeletevalue; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\.dae\OpenWithProgids"; ValueType: string; ValueName: "{#AppModelProgId}"; ValueData: ""; Flags: uninsdeletevalue; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\.dxf\OpenWithProgids"; ValueType: string; ValueName: "{#AppModelProgId}"; ValueData: ""; Flags: uninsdeletevalue; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\.off\OpenWithProgids"; ValueType: string; ValueName: "{#AppModelProgId}"; ValueData: ""; Flags: uninsdeletevalue; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\.x\OpenWithProgids"; ValueType: string; ValueName: "{#AppModelProgId}"; ValueData: ""; Flags: uninsdeletevalue; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\.3mf\OpenWithProgids"; ValueType: string; ValueName: "{#AppModelProgId}"; ValueData: ""; Flags: uninsdeletevalue; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\.drc\OpenWithProgids"; ValueType: string; ValueName: "{#AppModelProgId}"; ValueData: ""; Flags: uninsdeletevalue; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\.abc\OpenWithProgids"; ValueType: string; ValueName: "{#AppModelProgId}"; ValueData: ""; Flags: uninsdeletevalue; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\.usd\OpenWithProgids"; ValueType: string; ValueName: "{#AppModelProgId}"; ValueData: ""; Flags: uninsdeletevalue; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\.usda\OpenWithProgids"; ValueType: string; ValueName: "{#AppModelProgId}"; ValueData: ""; Flags: uninsdeletevalue; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\.usdc\OpenWithProgids"; ValueType: string; ValueName: "{#AppModelProgId}"; ValueData: ""; Flags: uninsdeletevalue; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\.usdz\OpenWithProgids"; ValueType: string; ValueName: "{#AppModelProgId}"; ValueData: ""; Flags: uninsdeletevalue; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\.step\OpenWithProgids"; ValueType: string; ValueName: "{#AppModelProgId}"; ValueData: ""; Flags: uninsdeletevalue; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\.stp\OpenWithProgids"; ValueType: string; ValueName: "{#AppModelProgId}"; ValueData: ""; Flags: uninsdeletevalue; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\.stpnc\OpenWithProgids"; ValueType: string; ValueName: "{#AppModelProgId}"; ValueData: ""; Flags: uninsdeletevalue; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\.p21\OpenWithProgids"; ValueType: string; ValueName: "{#AppModelProgId}"; ValueData: ""; Flags: uninsdeletevalue; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\.iges\OpenWithProgids"; ValueType: string; ValueName: "{#AppModelProgId}"; ValueData: ""; Flags: uninsdeletevalue; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\.igs\OpenWithProgids"; ValueType: string; ValueName: "{#AppModelProgId}"; ValueData: ""; Flags: uninsdeletevalue; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\.brep\OpenWithProgids"; ValueType: string; ValueName: "{#AppModelProgId}"; ValueData: ""; Flags: uninsdeletevalue; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\.xbf\OpenWithProgids"; ValueType: string; ValueName: "{#AppModelProgId}"; ValueData: ""; Flags: uninsdeletevalue; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\.ifc\OpenWithProgids"; ValueType: string; ValueName: "{#AppModelProgId}"; ValueData: ""; Flags: uninsdeletevalue; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\.vtk\OpenWithProgids"; ValueType: string; ValueName: "{#AppModelProgId}"; ValueData: ""; Flags: uninsdeletevalue; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\.vtp\OpenWithProgids"; ValueType: string; ValueName: "{#AppModelProgId}"; ValueData: ""; Flags: uninsdeletevalue; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\.vtu\OpenWithProgids"; ValueType: string; ValueName: "{#AppModelProgId}"; ValueData: ""; Flags: uninsdeletevalue; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\.vtr\OpenWithProgids"; ValueType: string; ValueName: "{#AppModelProgId}"; ValueData: ""; Flags: uninsdeletevalue; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\.vts\OpenWithProgids"; ValueType: string; ValueName: "{#AppModelProgId}"; ValueData: ""; Flags: uninsdeletevalue; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\.vtm\OpenWithProgids"; ValueType: string; ValueName: "{#AppModelProgId}"; ValueData: ""; Flags: uninsdeletevalue; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\.vtkhdf\OpenWithProgids"; ValueType: string; ValueName: "{#AppModelProgId}"; ValueData: ""; Flags: uninsdeletevalue; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\.exo\OpenWithProgids"; ValueType: string; ValueName: "{#AppModelProgId}"; ValueData: ""; Flags: uninsdeletevalue; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\.ex2\OpenWithProgids"; ValueType: string; ValueName: "{#AppModelProgId}"; ValueData: ""; Flags: uninsdeletevalue; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\.ncdf\OpenWithProgids"; ValueType: string; ValueName: "{#AppModelProgId}"; ValueData: ""; Flags: uninsdeletevalue; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\.vti\OpenWithProgids"; ValueType: string; ValueName: "{#AppModelProgId}"; ValueData: ""; Flags: uninsdeletevalue; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\.mha\OpenWithProgids"; ValueType: string; ValueName: "{#AppModelProgId}"; ValueData: ""; Flags: uninsdeletevalue; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\.mhd\OpenWithProgids"; ValueType: string; ValueName: "{#AppModelProgId}"; ValueData: ""; Flags: uninsdeletevalue; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\.nrrd\OpenWithProgids"; ValueType: string; ValueName: "{#AppModelProgId}"; ValueData: ""; Flags: uninsdeletevalue; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\.nhdr\OpenWithProgids"; ValueType: string; ValueName: "{#AppModelProgId}"; ValueData: ""; Flags: uninsdeletevalue; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\.dcm\OpenWithProgids"; ValueType: string; ValueName: "{#AppModelProgId}"; ValueData: ""; Flags: uninsdeletevalue; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\.vdb\OpenWithProgids"; ValueType: string; ValueName: "{#AppModelProgId}"; ValueData: ""; Flags: uninsdeletevalue; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\.pts\OpenWithProgids"; ValueType: string; ValueName: "{#AppModelProgId}"; ValueData: ""; Flags: uninsdeletevalue; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\.las\OpenWithProgids"; ValueType: string; ValueName: "{#AppModelProgId}"; ValueData: ""; Flags: uninsdeletevalue; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\.laz\OpenWithProgids"; ValueType: string; ValueName: "{#AppModelProgId}"; ValueData: ""; Flags: uninsdeletevalue; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\.pcd\OpenWithProgids"; ValueType: string; ValueName: "{#AppModelProgId}"; ValueData: ""; Flags: uninsdeletevalue; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\.bpf\OpenWithProgids"; ValueType: string; ValueName: "{#AppModelProgId}"; ValueData: ""; Flags: uninsdeletevalue; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\.slpk\OpenWithProgids"; ValueType: string; ValueName: "{#AppModelProgId}"; ValueData: ""; Flags: uninsdeletevalue; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\.splat\OpenWithProgids"; ValueType: string; ValueName: "{#AppModelProgId}"; ValueData: ""; Flags: uninsdeletevalue; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\.spz\OpenWithProgids"; ValueType: string; ValueName: "{#AppModelProgId}"; ValueData: ""; Flags: uninsdeletevalue; Tasks: associatefiles; Components: modelviewer
#endif
Root: HKA; Subkey: "Software\Classes\.jpg\OpenWithProgids"; ValueType: string; ValueName: "{#AppImageProgId}"; ValueData: ""; Flags: uninsdeletevalue; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\.jpe\OpenWithProgids"; ValueType: string; ValueName: "{#AppImageProgId}"; ValueData: ""; Flags: uninsdeletevalue; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\.jpeg\OpenWithProgids"; ValueType: string; ValueName: "{#AppImageProgId}"; ValueData: ""; Flags: uninsdeletevalue; Tasks: associatefiles
Root: HKA; Subkey: "Software\RegisteredApplications"; ValueType: string; ValueName: "{#AppDisplayName}"; ValueData: "Software\Classes\Applications\{#AppExeName}\Capabilities"; Flags: uninsdeletevalue; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".3fr"; ValueData: ""; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".ari"; ValueData: ""; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".arw"; ValueData: ""; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".avif"; ValueData: ""; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".bay"; ValueData: ""; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".jpg"; ValueData: ""; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".bmp"; ValueData: ""; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".cap"; ValueData: ""; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".cr2"; ValueData: ""; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".cr3"; ValueData: ""; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".crw"; ValueData: ""; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".cur"; ValueData: ""; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".dcr"; ValueData: ""; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".dcs"; ValueData: ""; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".dds"; ValueData: ""; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".dib"; ValueData: ""; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".dng"; ValueData: ""; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".drf"; ValueData: ""; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".erf"; ValueData: ""; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".exif"; ValueData: ""; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".exr"; ValueData: ""; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".fff"; ValueData: ""; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".gif"; ValueData: ""; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".gpr"; ValueData: ""; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".hdr"; ValueData: ""; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".heic"; ValueData: ""; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".heif"; ValueData: ""; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".ico"; ValueData: ""; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".iiq"; ValueData: ""; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".jfif"; ValueData: ""; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".jpe"; ValueData: ""; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".jpeg"; ValueData: ""; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".jxl"; ValueData: ""; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".k25"; ValueData: ""; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".kdc"; ValueData: ""; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".mdc"; ValueData: ""; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".mef"; ValueData: ""; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".mos"; ValueData: ""; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".mrw"; ValueData: ""; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".nef"; ValueData: ""; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".nrw"; ValueData: ""; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".orf"; ValueData: ""; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".pbm"; ValueData: ""; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".pef"; ValueData: ""; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".pcx"; ValueData: ""; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".pgm"; ValueData: ""; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".png"; ValueData: ""; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".ppm"; ValueData: ""; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".psb"; ValueData: ""; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".psd"; ValueData: ""; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".ptx"; ValueData: ""; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".pxn"; ValueData: ""; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".qoi"; ValueData: ""; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".raf"; ValueData: ""; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".raw"; ValueData: ""; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".rw2"; ValueData: ""; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".rwl"; ValueData: ""; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".sr2"; ValueData: ""; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".srf"; ValueData: ""; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".srw"; ValueData: ""; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".svg"; ValueData: ""; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".svgz"; ValueData: ""; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".tga"; ValueData: ""; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".tif"; ValueData: ""; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".tiff"; ValueData: ""; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".webp"; ValueData: ""; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".wmf"; ValueData: ""; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".x3f"; ValueData: ""; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".xbm"; ValueData: ""; Tasks: associatefiles
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".xpm"; ValueData: ""; Tasks: associatefiles
#if IncludeOptionalComponents
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".stl"; ValueData: ""; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".ply"; ValueData: ""; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".obj"; ValueData: ""; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".gltf"; ValueData: ""; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".glb"; ValueData: ""; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".3ds"; ValueData: ""; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".wrl"; ValueData: ""; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".vrml"; ValueData: ""; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".mdl"; ValueData: ""; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".fbx"; ValueData: ""; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".dae"; ValueData: ""; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".dxf"; ValueData: ""; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".off"; ValueData: ""; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".x"; ValueData: ""; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".3mf"; ValueData: ""; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".drc"; ValueData: ""; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".abc"; ValueData: ""; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".usd"; ValueData: ""; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".usda"; ValueData: ""; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".usdc"; ValueData: ""; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".usdz"; ValueData: ""; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".step"; ValueData: ""; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".stp"; ValueData: ""; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".stpnc"; ValueData: ""; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".p21"; ValueData: ""; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".iges"; ValueData: ""; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".igs"; ValueData: ""; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".brep"; ValueData: ""; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".xbf"; ValueData: ""; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".ifc"; ValueData: ""; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".vtk"; ValueData: ""; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".vtp"; ValueData: ""; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".vtu"; ValueData: ""; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".vtr"; ValueData: ""; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".vts"; ValueData: ""; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".vtm"; ValueData: ""; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".vtkhdf"; ValueData: ""; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".exo"; ValueData: ""; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".ex2"; ValueData: ""; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".ncdf"; ValueData: ""; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".vti"; ValueData: ""; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".mha"; ValueData: ""; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".mhd"; ValueData: ""; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".nrrd"; ValueData: ""; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".nhdr"; ValueData: ""; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".dcm"; ValueData: ""; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".vdb"; ValueData: ""; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".pts"; ValueData: ""; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".las"; ValueData: ""; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".laz"; ValueData: ""; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".pcd"; ValueData: ""; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".bpf"; ValueData: ""; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".slpk"; ValueData: ""; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".splat"; ValueData: ""; Tasks: associatefiles; Components: modelviewer
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".spz"; ValueData: ""; Tasks: associatefiles; Components: modelviewer
#endif

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(AppDisplayName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{app}\{#AppExeName}"; Parameters: "--shutdown"; Flags: runhidden waituntilterminated skipifdoesntexist; RunOnceId: "ShutdownMangosteen"

[Code]
#if IncludeOptionalComponents
procedure RemoveModelAssociation(RegistryRoot: Integer; Extension: String);
begin
  RegDeleteValue(RegistryRoot, 'Software\Classes\' + Extension + '\OpenWithProgids', '{#AppModelProgId}');
  RegDeleteValue(RegistryRoot, 'Software\Classes\Applications\{#AppExeName}\Capabilities\FileAssociations', Extension);
  RegDeleteValue(RegistryRoot, 'Software\Classes\Applications\{#AppExeName}\SupportedTypes', Extension);
end;

procedure RemoveDeselectedOptionalComponents;
var
  RegistryRoot: Integer;
begin
  if IsAdminInstallMode then
    RegistryRoot := HKEY_LOCAL_MACHINE
  else
    RegistryRoot := HKEY_CURRENT_USER;

  if not WizardIsComponentSelected('modelviewer') then
  begin
    DelTree(ExpandConstant('{app}\components\model-viewer'), True, True, True);
    RegDeleteKeyIncludingSubkeys(RegistryRoot, 'Software\Classes\{#AppModelProgId}');
    RemoveModelAssociation(RegistryRoot, '.stl');
    RemoveModelAssociation(RegistryRoot, '.ply');
    RemoveModelAssociation(RegistryRoot, '.obj');
    RemoveModelAssociation(RegistryRoot, '.gltf');
    RemoveModelAssociation(RegistryRoot, '.glb');
    RemoveModelAssociation(RegistryRoot, '.3ds');
    RemoveModelAssociation(RegistryRoot, '.wrl');
    RemoveModelAssociation(RegistryRoot, '.vrml');
    RemoveModelAssociation(RegistryRoot, '.mdl');
    RemoveModelAssociation(RegistryRoot, '.fbx');
    RemoveModelAssociation(RegistryRoot, '.dae');
    RemoveModelAssociation(RegistryRoot, '.dxf');
    RemoveModelAssociation(RegistryRoot, '.off');
    RemoveModelAssociation(RegistryRoot, '.x');
    RemoveModelAssociation(RegistryRoot, '.3mf');
    RemoveModelAssociation(RegistryRoot, '.drc');
    RemoveModelAssociation(RegistryRoot, '.abc');
    RemoveModelAssociation(RegistryRoot, '.usd');
    RemoveModelAssociation(RegistryRoot, '.usda');
    RemoveModelAssociation(RegistryRoot, '.usdc');
    RemoveModelAssociation(RegistryRoot, '.usdz');
    RemoveModelAssociation(RegistryRoot, '.step');
    RemoveModelAssociation(RegistryRoot, '.stp');
    RemoveModelAssociation(RegistryRoot, '.stpnc');
    RemoveModelAssociation(RegistryRoot, '.p21');
    RemoveModelAssociation(RegistryRoot, '.iges');
    RemoveModelAssociation(RegistryRoot, '.igs');
    RemoveModelAssociation(RegistryRoot, '.brep');
    RemoveModelAssociation(RegistryRoot, '.xbf');
    RemoveModelAssociation(RegistryRoot, '.ifc');
    RemoveModelAssociation(RegistryRoot, '.vtk');
    RemoveModelAssociation(RegistryRoot, '.vtp');
    RemoveModelAssociation(RegistryRoot, '.vtu');
    RemoveModelAssociation(RegistryRoot, '.vtr');
    RemoveModelAssociation(RegistryRoot, '.vts');
    RemoveModelAssociation(RegistryRoot, '.vtm');
    RemoveModelAssociation(RegistryRoot, '.vtkhdf');
    RemoveModelAssociation(RegistryRoot, '.exo');
    RemoveModelAssociation(RegistryRoot, '.ex2');
    RemoveModelAssociation(RegistryRoot, '.ncdf');
    RemoveModelAssociation(RegistryRoot, '.vti');
    RemoveModelAssociation(RegistryRoot, '.mha');
    RemoveModelAssociation(RegistryRoot, '.mhd');
    RemoveModelAssociation(RegistryRoot, '.nrrd');
    RemoveModelAssociation(RegistryRoot, '.nhdr');
    RemoveModelAssociation(RegistryRoot, '.dcm');
    RemoveModelAssociation(RegistryRoot, '.vdb');
    RemoveModelAssociation(RegistryRoot, '.pts');
    RemoveModelAssociation(RegistryRoot, '.las');
    RemoveModelAssociation(RegistryRoot, '.laz');
    RemoveModelAssociation(RegistryRoot, '.pcd');
    RemoveModelAssociation(RegistryRoot, '.bpf');
    RemoveModelAssociation(RegistryRoot, '.slpk');
    RemoveModelAssociation(RegistryRoot, '.splat');
    RemoveModelAssociation(RegistryRoot, '.spz');
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssInstall then
    RemoveDeselectedOptionalComponents;
end;
#endif

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
  ExistingApp: String;
  StartupCommand: String;
begin
  Result := '';
  ExistingApp := ExpandConstant('{app}\{#AppExeName}');
  if FileExists(ExistingApp) and
    RegQueryStringValue(
      HKEY_CURRENT_USER,
      'Software\Microsoft\Windows\CurrentVersion\Run',
      '{#AppDisplayName}',
      StartupCommand) and
    (Pos('--background', Lowercase(StartupCommand)) > 0) then
  begin
    Exec(ExistingApp, '--shutdown', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Sleep(250);
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
    RegDeleteValue(
      HKEY_CURRENT_USER,
      'Software\Microsoft\Windows\CurrentVersion\Run',
      '{#AppDisplayName}');
end;

