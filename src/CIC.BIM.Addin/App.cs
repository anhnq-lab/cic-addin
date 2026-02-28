using System.IO;
using System.Reflection;
using System.Windows.Media;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using Autodesk.Windows;
using CIC.BIM.Addin.Analytics.Commands;
using CIC.BIM.Addin.Analytics.Services;

namespace CIC.BIM.Addin;

/// <summary>
/// CIC Tool Add-in Entry Point
/// </summary>
public class App : IExternalApplication
{
    private const string TabName = "CIC Tools";
    private const string PanelFM = "Quản lý Vận hành";
    private const string PanelAnalytics = "Phân tích Hiệu suất";

    private static bool _colorApplied = false;
    private ActivityTracker? _activityTracker;

    public Result OnStartup(UIControlledApplication application)
    {
        try
        {
            // ═══ Assembly resolve for NuGet dependencies (ClosedXML etc.) ═══
            AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve;

            // Create custom tab
            application.CreateRibbonTab(TabName);

            // ═══ Panel: Quản lý Dữ liệu ═══
            var panelData = application.CreateRibbonPanel(TabName, "Quản lý Dữ liệu");
            var assemblyPath = Assembly.GetExecutingAssembly().Location;
            var addinDir = Path.GetDirectoryName(assemblyPath)!;
            var toolsAssemblyPath = Path.Combine(addinDir, "CIC.BIM.Addin.Tools.dll");
            
            var btnParamManager = new PushButtonData(
                "ParamManager",
                "Quản lý\nTham số",
                toolsAssemblyPath,
                "CIC.BIM.Addin.Tools.Commands.ParamManagerCommand"
            )
            {
                ToolTip = "Quản lý tham số cho tất cả đối tượng trong mô hình BIM",
                LongDescription = "Công cụ quản lý dữ liệu & thông tin cốt lõi của CIC Add-in.\n" +
                    "Hỗ trợ đa bộ môn: Kết cấu, Kiến trúc, Cơ điện, Đường ống.\n" +
                    "Gán tham số tự động, chỉnh sửa thủ công, xuất Excel.",
                LargeImage = LoadIcon("icon_assign_params.png")
            };
            panelData.AddItem(btnParamManager);

            // Button 2: Tự động nối cấu kiện
            var btnAutoJoint = new PushButtonData(
                "AutoJoint",
                "Tự động\nNối CK",
                toolsAssemblyPath,
                "CIC.BIM.Addin.Tools.Commands.AutoJointCommand"
            )
            {
                ToolTip = "Tự động nối hình học giữa các cấu kiện giao nhau",
                LongDescription = "Nối (Join) tự động cho cấu kiện kết cấu & kiến trúc.\n" +
                    "Thiết lập thứ tự ưu tiên: Cột > Dầm > Tường > Sàn.\n" +
                    "Hỗ trợ Join, UnJoin, Switch Join Order.",
                LargeImage = LoadIcon("icon_kc.png")
            };
            panelData.AddItem(btnAutoJoint);

            // Button 3: Tô màu đối tượng
            var btnColorOverride = new PushButtonData(
                "ColorOverride",
                "Tô màu\nĐối tượng",
                toolsAssemblyPath,
                "CIC.BIM.Addin.Tools.Commands.ColorOverrideCommand"
            )
            {
                ToolTip = "Tô màu đối tượng theo Category để dễ nhận biết",
                LongDescription = "Gán màu cho từng Category trong View hiện tại.\n" +
                    "Hỗ trợ chọn màu tùy chỉnh, bật/tắt từng category.\n" +
                    "Có thể Reset về màu gốc bất cứ lúc nào.",
                LargeImage = LoadIcon("icon_assign_params.png")
            };
            panelData.AddItem(btnColorOverride);

            // ═══ Panel: Kết cấu ═══
            var panelKC = application.CreateRibbonPanel(TabName, "Kết cấu");

            // Button: Thống kê Ván khuôn
            var btnFormwork = new PushButtonData(
                "Formwork",
                "Thống kê\nVán khuôn",
                toolsAssemblyPath,
                "CIC.BIM.Addin.Tools.Commands.FormworkCommand"
            )
            {
                ToolTip = "Tính diện tích ván khuôn B3.2 và tạo ván khuôn 3D",
                LongDescription = "Tính diện tích ván khuôn theo nguyên tắc:\n" +
                    "Dầm (đáy+bên), Cột (bên), Sàn (đáy), Móng (đáy+bên).\n" +
                    "Tự trừ giao nhau. Tạo DirectShape VK 3D màu nâu.",
                LargeImage = LoadIcon("icon_formwork.png")
            };
            panelKC.AddItem(btnFormwork);

            // Button: Trát tường
            var btnPlaster = new PushButtonData(
                "Plaster",
                "Trát tường\nTheo phòng",
                toolsAssemblyPath,
                "CIC.BIM.Addin.Tools.Commands.PlasterCommand"
            )
            {
                ToolTip = "Tạo lớp trát tường hoàn thiện theo phòng",
                LongDescription = "Chọn phòng → tự động tạo tường trát bao quanh.\n" +
                    "Hỗ trợ tùy chỉnh Wall Type và Floor Type.",
                LargeImage = LoadIcon("icon_plaster.png")
            };
            panelKC.AddItem(btnPlaster);

            // Button: Room Bounding
            var btnRoomBounding = new PushButtonData(
                "SetRoomBounding",
                "Bật Room\nBounding",
                toolsAssemblyPath,
                "CIC.BIM.Addin.Tools.Commands.SetRoomBoundingCommand"
            )
            {
                ToolTip = "Bật Room Bounding cho link instances và cột",
                LongDescription = "Tự động bật Room Bounding cho tất cả file link và cột.\n" +
                    "Room sẽ nhận diện tường/cột từ file link kết cấu.\n" +
                    "Chạy trước khi tạo Room hoặc trước khi chạy Trát tường.",
                LargeImage = LoadIcon("icon_room_bounding.png")
            };
            panelKC.AddItem(btnRoomBounding);

            // ═══ Panel: Kiến trúc ═══
            var panelKT = application.CreateRibbonPanel(TabName, "Kiến trúc");

            // Button: Block từ CAD
            var btnBlockCad = new PushButtonData(
                "BlockCad",
                "Tách Block\ntừ CAD",
                toolsAssemblyPath,
                "CIC.BIM.Addin.Tools.Commands.BlockCadCommand"
            )
            {
                ToolTip = "Tách và phân loại block từ bản vẽ CAD link",
                LongDescription = "Quét file CAD link, liệt kê danh sách block.\n" +
                    "Hỗ trợ phân loại và xử lý block.",
                LargeImage = LoadIcon("icon_block_cad.png")
            };
            panelKT.AddItem(btnBlockCad);

            // ═══ Panel: Cơ điện (MEP) ═══
            var panelMEP = application.CreateRibbonPanel(TabName, "Cơ điện");

            // Button: Tạo ống gió từ CAD
            var btnDuct = new PushButtonData(
                "DuctFromCad",
                "Ống gió\ntừ CAD",
                toolsAssemblyPath,
                "CIC.BIM.Addin.Tools.Commands.DuctFromCadCommand"
            )
            {
                ToolTip = "Tạo ống gió từ bản vẽ CAD",
                LongDescription = "Đọc đường dẫn ống gió từ CAD link.\n" +
                    "Tự động tạo Duct trong Revit.",
                LargeImage = LoadIcon("icon_duct.png")
            };
            panelMEP.AddItem(btnDuct);

            // Button: Kiểm tra độ dốc ống
            var btnPipeSlope = new PushButtonData(
                "PipeSlope",
                "Kiểm tra\nĐộ dốc ống",
                toolsAssemblyPath,
                "CIC.BIM.Addin.Tools.Commands.PipeSlopeCommand"
            )
            {
                ToolTip = "Kiểm tra và hiển thị độ dốc ống nước",
                LongDescription = "Quét hệ thống ống, kiểm tra slope.\n" +
                    "Cảnh báo ống không đạt độ dốc tối thiểu.",
                LargeImage = LoadIcon("icon_pipe_slope.png")
            };
            panelMEP.AddItem(btnPipeSlope);

            // ═══ Panel: AI Hỗ trợ ═══
            try
            {
                var panelAI = application.CreateRibbonPanel(TabName, "AI Ho tro");

                var btnAIChat = new PushButtonData(
                    "AIChat",
                    "AI CIC",
                    toolsAssemblyPath,
                    "CIC.BIM.Addin.Tools.Commands.AIChatCommand"
                )
                {
                    ToolTip = "Hoi dap thong tin mo hinh BIM bang AI (Google Gemini)",
                    LongDescription = "Su dung AI (Google Gemini) de tra loi cau hoi\n" +
                        "ve thong tin trong mo hinh Revit.\n" +
                        "Vi du: so luong cau kien, the tich, dien tich, phong...",
                    LargeImage = LoadIcon("icon_ai_chat.png")
                };
                panelAI.AddItem(btnAIChat);
            }
            catch (Exception aiEx)
            {
                System.Diagnostics.Debug.WriteLine($"[CIC AI Panel] Init failed: {aiEx.Message}");
            }

            // ═══ Panel: Quản lý Vận hành ═══
            var panelFM = application.CreateRibbonPanel(TabName, PanelFM);

            var fmAssemblyPath = Path.Combine(addinDir, "CIC.BIM.Addin.FacilityMgmt.dll");

            // Button 1: Gán tham số FM
            var btnAssignParams = new PushButtonData(
                "AssignFMParams",
                "Gán tham số\nVận hành",
                fmAssemblyPath,
                "CIC.BIM.Addin.FacilityMgmt.Commands.AssignFMParamsCommand"
            )
            {
                ToolTip = "Tạo và gán 8 Shared Parameters phục vụ quản lý vận hành vào các MEP categories",
                LongDescription = "Tự động tạo Shared Parameter file và bind các tham số FM " +
                    "(AssetCode, Category, Location, Manufacturer, Model, MaintenanceCycle, Status, Condition) " +
                    "vào các categories thiết bị MEP trong model.",
                LargeImage = LoadIcon("icon_assign_params.png")
            };
            panelFM.AddItem(btnAssignParams);

            // Button 2: Điền dữ liệu FM
            var btnFillData = new PushButtonData(
                "FillFMData",
                "Điền dữ liệu\nVận hành",
                fmAssemblyPath,
                "CIC.BIM.Addin.FacilityMgmt.Commands.FillFMDataCommand"
            )
            {
                ToolTip = "Tự động điền Location (từ Room/Space), Category, và AssetCode cho các thiết bị MEP",
                LongDescription = "Quét toàn bộ thiết bị MEP trong model, tự động:\n" +
                    "• Lấy tên Room/Space → gán vào Location\n" +
                    "• Map Revit Category → FM Category (HVAC, Cơ điện, PCCC...)\n" +
                    "• Sinh mã tài sản AssetCode theo format chuẩn",
                LargeImage = LoadIcon("icon_fill_data.png")
            };
            panelFM.AddItem(btnFillData);

            // Button 3: Xuất báo cáo FM
            var btnExport = new PushButtonData(
                "ExportFMReport",
                "Xuất báo cáo\nVận hành",
                fmAssemblyPath,
                "CIC.BIM.Addin.FacilityMgmt.Commands.ExportFMReportCommand"
            )
            {
                ToolTip = "Xuất danh sách tài sản/thiết bị ra file Excel",
                LongDescription = "Export toàn bộ thiết bị MEP có tham số FM ra file Excel, " +
                    "bao gồm: Mã tài sản, Tên, Phân loại, Vị trí, Nhà sản xuất, Model, " +
                    "Chu kỳ bảo trì, Trạng thái, Tình trạng.",
                LargeImage = LoadIcon("icon_export_report.png")
            };
            panelFM.AddItem(btnExport);

            // ═══ Analytics: Silent tracking for all users ═══
            // Wrapped in try-catch so FM module still works if Analytics fails
            try
            {
                _activityTracker = new ActivityTracker();
                _activityTracker.StartTracking(application);
                DashboardCommand.Tracker = _activityTracker;

                // ═══ Dashboard panel ẩn tạm thời - chỉ tracking ẩn ═══
                // Panel Analytics Dashboard đã được ẩn theo yêu cầu.
                // Tracking hiệu suất vẫn hoạt động ẩn phía sau.
            }
            catch (Exception analyticsEx)
            {
                // Analytics failure is non-critical — log and continue
                System.Diagnostics.Debug.WriteLine($"[CIC Analytics] Init failed: {analyticsEx.Message}");
            }

            // ═══ Register Idling event for tab color ═══
            // Tab color MUST be applied after UI is fully initialized
            application.Idling += OnIdlingApplyColor;

            return Result.Succeeded;
        }
        catch (Exception ex)
        {
            Autodesk.Revit.UI.TaskDialog.Show("CIC Tool", $"Lỗi khởi tạo: {ex.Message}");
            return Result.Failed;
        }
    }

    public Result OnShutdown(UIControlledApplication application)
    {
        // Stop analytics tracking
        _activityTracker?.StopTracking();
        _activityTracker?.Dispose();
        _activityTracker = null;

        application.Idling -= OnIdlingApplyColor;
        AppDomain.CurrentDomain.AssemblyResolve -= OnAssemblyResolve;
        return Result.Succeeded;
    }

    /// <summary>
    /// Resolve assemblies from the add-in's directory (for NuGet packages like ClosedXML).
    /// Revit doesn't automatically look in the add-in folder for dependencies.
    /// </summary>
    private static Assembly? OnAssemblyResolve(object? sender, ResolveEventArgs args)
    {
        try
        {
            var assemblyName = new AssemblyName(args.Name).Name;
            if (string.IsNullOrEmpty(assemblyName)) return null;

            var addinDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            if (string.IsNullOrEmpty(addinDir)) return null;

            var assemblyPath = Path.Combine(addinDir, assemblyName + ".dll");
            if (File.Exists(assemblyPath))
            {
                return Assembly.LoadFrom(assemblyPath);
            }
        }
        catch { }

        return null;
    }

    /// <summary>
    /// Apply tab color on first Idling event (UI is fully ready by then).
    /// </summary>
    private void OnIdlingApplyColor(object? sender, IdlingEventArgs e)
    {
        if (_colorApplied) return;
        _colorApplied = true;

        try
        {
            var ribbon = ComponentManager.Ribbon;
            if (ribbon == null) return;

            foreach (var tab in ribbon.Tabs)
            {
                if (tab.Title == TabName || tab.Id == TabName)
                {
                    // Use reflection to set TabTheme (internal Autodesk API)
                    var adWindowsAssembly = typeof(RibbonControl).Assembly;
                    var tabThemeType = adWindowsAssembly.GetType("Autodesk.Internal.Windows.TabTheme");

                    if (tabThemeType != null)
                    {
                        object theme = Activator.CreateInstance(tabThemeType)!;

                        // Sky blue colors
                        var skyBlue = new SolidColorBrush(Color.FromRgb(0x00, 0x9F, 0xDB));
                        skyBlue.Freeze();
                        var white = new SolidColorBrush(Colors.White);
                        white.Freeze();
                        var darkBlue = new SolidColorBrush(Color.FromRgb(0x00, 0x7B, 0xB8));
                        darkBlue.Freeze();
                        var lightBlue = new SolidColorBrush(Color.FromRgb(0xE0, 0xF0, 0xFF));
                        lightBlue.Freeze();

                        // Try setting all known TabTheme properties
                        TrySetProp(theme, "TabHeaderBackground", skyBlue);
                        TrySetProp(theme, "TabHeaderForeground", white);
                        TrySetProp(theme, "PanelTitleBarBackground", darkBlue);
                        TrySetProp(theme, "PanelBackground", lightBlue);
                        TrySetProp(theme, "RibbonTabBackground", skyBlue);
                        TrySetProp(theme, "ActiveTabBackground", skyBlue);
                        TrySetProp(theme, "InactiveTabBackground", skyBlue);

                        // Apply using reflection to bypass type checking
                        var themeProp = tab.GetType().GetProperty("Theme",
                            BindingFlags.Public | BindingFlags.Instance);
                        if (themeProp != null)
                        {
                            themeProp.SetValue(tab, theme);
                        }
                    }
                    break;
                }
            }
        }
        catch
        {
            // Cosmetic feature — silently fail
        }
    }

    private static void TrySetProp(object instance, string propName, object value)
    {
        try
        {
            var prop = instance.GetType().GetProperty(propName, BindingFlags.Public | BindingFlags.Instance);
            if (prop != null && prop.CanWrite)
            {
                prop.SetValue(instance, value);
            }
        }
        catch { }
    }

    private static System.Windows.Media.Imaging.BitmapImage? LoadIcon(string filename)
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = $"CIC.BIM.Addin.Resources.{filename}";
            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null) return null;

            var bmp = new System.Windows.Media.Imaging.BitmapImage();
            bmp.BeginInit();
            bmp.StreamSource = stream;
            bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }
}

