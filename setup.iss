; ═══════════════════════════════════════════════════════════
; CIC BIM Addin - Inno Setup Script
; Tạo bộ cài EXE cho Revit 2024 & 2025
; ═══════════════════════════════════════════════════════════

#define MyAppName "CIC BIM Addin"
#define MyAppVersion "1.0.5"
#define MyAppPublisher "CIC Technology Joint Stock Company"
#define MyAppURL "https://github.com/anhnq-lab/cic-addin"

[Setup]
AppId={{8F4E2A1B-3C5D-4E6F-A7B8-9C0D1E2F3A4B}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} v{#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
DefaultDirName={userappdata}\CIC BIM Addin
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
DisableDirPage=yes
OutputDir=dist
OutputBaseFilename=CIC_BIM_Addin_Setup_v{#MyAppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
UninstallDisplayName={#MyAppName}
UninstallFilesDir={userappdata}\CIC BIM Addin\uninstall

; Vietnamese messages
WizardImageStretch=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Types]
Name: "full"; Description: "Cài đặt đầy đủ (Revit 2024 + 2025)"
Name: "revit2024"; Description: "Chỉ cài cho Revit 2024"
Name: "revit2025"; Description: "Chỉ cài cho Revit 2025"
Name: "custom"; Description: "Tùy chọn"; Flags: iscustom

[Components]
Name: "revit2024"; Description: "CIC Tools cho Revit 2024"; Types: full revit2024 custom
Name: "revit2025"; Description: "CIC Tools cho Revit 2025"; Types: full revit2025 custom

; ─── Revit 2024 Files ───
[Files]
; DLLs và dependencies cho Revit 2024
Source: "bin\Release\Revit2024\*"; DestDir: "{userappdata}\Autodesk\Revit\Addins\2024\CICTool"; Components: revit2024; Flags: ignoreversion recursesubdirs createallsubdirs

; .addin manifest cho Revit 2024
Source: "bin\Release\Revit2024\CIC.BIM.Addin.2024.addin"; DestDir: "{userappdata}\Autodesk\Revit\Addins\2024"; DestName: "CIC.BIM.Addin.addin"; Components: revit2024; Flags: ignoreversion

; ─── Revit 2025 Files ───
; DLLs và dependencies cho Revit 2025
Source: "bin\Release\Revit2025\*"; DestDir: "{userappdata}\Autodesk\Revit\Addins\2025\CICTool"; Components: revit2025; Flags: ignoreversion recursesubdirs createallsubdirs

; .addin manifest cho Revit 2025
Source: "bin\Release\Revit2025\CIC.BIM.Addin.addin"; DestDir: "{userappdata}\Autodesk\Revit\Addins\2025"; DestName: "CIC.BIM.Addin.addin"; Components: revit2025; Flags: ignoreversion

[UninstallDelete]
; Xóa sạch thư mục CICTool khi gỡ cài đặt
Type: filesandordirs; Name: "{userappdata}\Autodesk\Revit\Addins\2024\CICTool"
Type: files; Name: "{userappdata}\Autodesk\Revit\Addins\2024\CIC.BIM.Addin.addin"
Type: filesandordirs; Name: "{userappdata}\Autodesk\Revit\Addins\2025\CICTool"
Type: files; Name: "{userappdata}\Autodesk\Revit\Addins\2025\CIC.BIM.Addin.addin"

[Messages]
WelcomeLabel2=Chương trình sẽ cài đặt [name] v[ver] lên máy tính của bạn.%n%nCIC Tools cung cấp các công cụ BIM cho Revit:%n- Quản lý tham số%n- Thống kê vật liệu%n- Facility Management%n- AI hỗ trợ%n%nVui lòng đóng Revit trước khi cài đặt.
FinishedLabelNoIcons=Cài đặt [name] hoàn tất!%n%nVui lòng khởi động lại Revit để sử dụng.%nTab "CIC Tools" sẽ xuất hiện trên Ribbon.

[Code]
// Kiểm tra Revit đang chạy không
function IsRevitRunning(): Boolean;
var
  ResultCode: Integer;
begin
  Result := False;
  if Exec('tasklist', '/FI "IMAGENAME eq Revit.exe" /NH', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
  begin
    // tasklist always returns 0, we check via FindWindowByClassName instead
    Result := FindWindowByClassName('Afx:RibbonBar:4057e8c0:0:0:0') <> 0;
  end;
end;

function InitializeSetup(): Boolean;
begin
  Result := True;
  if IsRevitRunning() then
  begin
    MsgBox('Revit đang chạy! Vui lòng đóng Revit trước khi cài đặt CIC BIM Addin.', mbError, MB_OK);
    Result := False;
  end;
end;
