using System.IO;
using System.Reflection;
using System.Windows.Media;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using Autodesk.Windows;
using CIC.BIM.Addin.Analytics.Commands;
using CIC.BIM.Addin.Analytics.Services;
using CIC.BIM.Addin.Bridge;

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
    private RevitBridgeServer? _bridgeServer;
    private RevitApiHandler? _bridgeHandler;

    public Result OnStartup(UIControlledApplication application)
    {
        try
        {
            // ═══ Assembly resolve for NuGet dependencies (ClosedXML etc.) ═══
            AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve;

            // Create custom tab
            application.CreateRibbonTab(TabName);

            var assemblyPath = Assembly.GetExecutingAssembly().Location;
            var addinDir = Path.GetDirectoryName(assemblyPath)!;
            var toolsAssemblyPath = Path.Combine(addinDir, "CIC.BIM.Addin.Tools.dll");
            var fmAssemblyPath = Path.Combine(addinDir, "CIC.BIM.Addin.FacilityMgmt.dll");

            // ═════ 1. DATA & QUẢN LÝ ═════
            var panelData = application.CreateRibbonPanel(TabName, "Data & Quản lý");
            
            var btnParamManager = new PushButtonData("ParamManager", "Quản lý\nTham số", toolsAssemblyPath, "CIC.BIM.Addin.Tools.Commands.ParamManagerCommand")
            {
                ToolTip = "Quản lý tham số cho tất cả đối tượng trong mô hình BIM",
                LongDescription = "Công cụ quản lý dữ liệu & thông tin cốt lõi của CIC Add-in.\nHỗ trợ đa bộ môn: Kết cấu, Kiến trúc, Cơ điện, Đường ống.\nGán tham số tự động, chỉnh sửa thủ công, xuất Excel.",
                LargeImage = LoadIcon("icon_assign_params.png")
            };
            panelData.AddItem(btnParamManager);

            // ═════ 2. DỰNG HÌNH ═════
            var panelModelling = application.CreateRibbonPanel(TabName, "Dựng hình");
            
            var btnAutoJoint = new PushButtonData("AutoJoint", "Nối\nCấu Kiện", toolsAssemblyPath, "CIC.BIM.Addin.Tools.Commands.AutoJointCommand")
            {
                ToolTip = "Tự động nối hình học giữa các cấu kiện giao nhau",
                LongDescription = "Nối (Join) tự động cho cấu kiện kết cấu & kiến trúc.\nThiết lập thứ tự ưu tiên: Cột > Dầm > Tường > Sàn.\nHỗ trợ Join, UnJoin, Switch Join Order.",
                LargeImage = LoadIcon("icon_kc.png")
            };
            panelModelling.AddItem(btnAutoJoint);

            var btnPlaster = new PushButtonData("Plaster", "Hoàn\nThiện", toolsAssemblyPath, "CIC.BIM.Addin.Tools.Commands.FinishingCommand")
            {
                ToolTip = "Tạo lớp hoàn thiện (trát, sơn, ốp) theo phòng",
                LongDescription = "Tạo Room tự động, tường/sàn/dầm/cột hoàn thiện.\nHỗ trợ tùy chỉnh Wall Type, Floor Type và nhiều tùy chọn.",
                LargeImage = LoadIcon("icon_plaster.png")
            };
            panelModelling.AddItem(btnPlaster);

            var pullDownUtilData = new PulldownButtonData("ModellingUtils", "Tiện\ních") 
            { 
                LargeImage = LoadIcon("icon_room_bounding.png"),
                ToolTip = "Các tiện ích hỗ trợ dựng hình"
            };
            if (panelModelling.AddItem(pullDownUtilData) is PulldownButton pullDownUtil)
            {
                var btnRoomBounding = new PushButtonData("SetRoomBounding", "Bật Room Bounding", toolsAssemblyPath, "CIC.BIM.Addin.Tools.Commands.SetRoomBoundingCommand")
                {
                    ToolTip = "Bật Room Bounding cho link instances và cột",
                    LongDescription = "Tự động bật Room Bounding cho tất cả file link và cột.\nTrợ giúp nhận diện phòng để chạy chức năng Hoàn Thiện.",
                    Image = LoadIcon("icon_room_bounding.png"),
                    LargeImage = LoadIcon("icon_room_bounding.png")
                };
                pullDownUtil.AddPushButton(btnRoomBounding);
            }

            // ═════ 3. CAD TO BIM ═════
            var panelCAD = application.CreateRibbonPanel(TabName, "CAD to BIM");
            
            var pullDownCADData = new PulldownButtonData("CadTools", "Từ CAD") 
            { 
                LargeImage = LoadIcon("icon_block_cad.png"),
                ToolTip = "Các công cụ chuyển đổi dữ liệu từ file CAD"
            };
            if (panelCAD.AddItem(pullDownCADData) is PulldownButton pullDownCAD)
            {
                var btnBlockCad = new PushButtonData("BlockCad", "Tách Block từ CAD", toolsAssemblyPath, "CIC.BIM.Addin.Tools.Commands.BlockCadCommand")
                {
                    ToolTip = "Tách và phân loại block từ bản vẽ CAD link",
                    Image = LoadIcon("icon_block_cad.png"),
                    LargeImage = LoadIcon("icon_block_cad.png")
                };
                pullDownCAD.AddPushButton(btnBlockCad);

                var btnDuct = new PushButtonData("DuctFromCad", "Ống gió từ CAD", toolsAssemblyPath, "CIC.BIM.Addin.Tools.Commands.DuctFromCadCommand")
                {
                    ToolTip = "Tạo ống gió từ bản vẽ CAD",
                    Image = LoadIcon("icon_duct.png"),
                    LargeImage = LoadIcon("icon_duct.png")
                };
                pullDownCAD.AddPushButton(btnDuct);

                var btnCadAutoDraw = new PushButtonData("CadAutoDraw", "Auto-Draw từ CAD", toolsAssemblyPath, "CIC.BIM.Addin.Tools.Commands.CadAutoDrawCommand")
                {
                    ToolTip = "Tự động vẽ đối tượng Revit từ file CAD link",
                    LongDescription = "Scan layers/blocks từ CAD link → mapping sang đối tượng Revit.\nHỗ trợ: Wall, Column, Beam, Floor, Pipe, Duct, Cable Tray, Family Instance.",
                    Image = LoadIcon("icon_block_cad.png"),
                    LargeImage = LoadIcon("icon_block_cad.png")
                };
                pullDownCAD.AddPushButton(btnCadAutoDraw);
            }

            // ═════ 4. KIỂM TRA & HIỂN THỊ ═════
            var panelQAQC = application.CreateRibbonPanel(TabName, "Kiểm tra & Hiển thị");
            
            var btnColorOverride = new PushButtonData("ColorOverride", "Tô màu\nĐối tượng", toolsAssemblyPath, "CIC.BIM.Addin.Tools.Commands.ColorOverrideCommand")
            {
                ToolTip = "Tô màu đối tượng theo Category để dễ nhận biết",
                LargeImage = LoadIcon("icon_assign_params.png")
            };
            panelQAQC.AddItem(btnColorOverride);

            var btnPipeSlope = new PushButtonData("PipeSlope", "Kiểm tra\nĐộ dốc ống", toolsAssemblyPath, "CIC.BIM.Addin.Tools.Commands.PipeSlopeCommand")
            {
                ToolTip = "Kiểm tra và hiển thị độ dốc ống nước",
                LargeImage = LoadIcon("icon_pipe_slope.png")
            };
            panelQAQC.AddItem(btnPipeSlope);

            // ═════ 5. KHỐI LƯỢNG ═════
            var panelQTO = application.CreateRibbonPanel(TabName, "Khối lượng");
            
            var btnSmartQTO = new PushButtonData("SmartQTO", "Bóc KL\nBOQ", toolsAssemblyPath, "CIC.BIM.Addin.Tools.Commands.SmartQTOCommand")
            {
                ToolTip = "Trích xuất và tính toán khối lượng tự động ra file Excel BOQ",
                LongDescription = "Hỗ trợ bóc tách Thể tích bê tông, Diện tích ván khuôn, Xây trát...\nCho phép áp dụng toàn dự án hoặc chỉ cấu kiện đang chọn.",
                LargeImage = LoadIcon("icon_qto_excel.png"),
                Image = LoadIcon("icon_qto_excel.png")
            };
            
            var btnFormwork = new PushButtonData("Formwork", "Thống kê\nVán khuôn", toolsAssemblyPath, "CIC.BIM.Addin.Tools.Commands.FormworkCommand")
            {
                ToolTip = "Tính diện tích ván khuôn B3.2 và tạo ván khuôn 3D",
                LargeImage = LoadIcon("icon_formwork.png")
            };

            panelQTO.AddItem(btnSmartQTO);
            panelQTO.AddItem(btnFormwork);

            // ═════ 6. VẬN HÀNH (FM) ═════
            var panelFM = application.CreateRibbonPanel(TabName, "Vận hành (FM)");

            var btnAssignParams = new PushButtonData("AssignFMParams", "Gán tham số FM", fmAssemblyPath, "CIC.BIM.Addin.FacilityMgmt.Commands.AssignFMParamsCommand")
            {
                ToolTip = "Tạo và gán 8 Shared Parameters phục vụ quản lý vận hành",
                Image = LoadIcon("icon_assign_params.png")
            };
            
            var btnFillData = new PushButtonData("FillFMData", "Điền DL Vận hành", fmAssemblyPath, "CIC.BIM.Addin.FacilityMgmt.Commands.FillFMDataCommand")
            {
                ToolTip = "Tự động điền Location, Category, AssetCode",
                Image = LoadIcon("icon_fill_data.png")
            };
            
            var btnExport = new PushButtonData("ExportFMReport", "Xuất BC Vận hành", fmAssemblyPath, "CIC.BIM.Addin.FacilityMgmt.Commands.ExportFMReportCommand")
            {
                ToolTip = "Xuất danh sách thiết bị ra Excel",
                Image = LoadIcon("icon_export_report.png")
            };

            panelFM.AddStackedItems(btnAssignParams, btnFillData, btnExport);

            // ═════ 7. AI ═════
            try
            {
                var panelAI = application.CreateRibbonPanel(TabName, "AI Hỗ trợ");

                var btnAIChat = new PushButtonData("AIChat", "AI CIC", toolsAssemblyPath, "CIC.BIM.Addin.Tools.Commands.AIChatCommand")
                {
                    ToolTip = "Hỏi đáp thông tin mô hình BIM bằng AI (Google Gemini)",
                    LongDescription = "Sử dụng AI để trả lời câu hỏi về thông tin trong mô hình Revit.\nVí dụ: số lượng cấu kiện, thể tích...",
                    LargeImage = LoadIcon("icon_ai_chat.png")
                };
                panelAI.AddItem(btnAIChat);
            }
            catch (Exception aiEx)
            {
                System.Diagnostics.Debug.WriteLine($"[CIC AI Panel] Init failed: {aiEx.Message}");
            }

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

            // ═══ Revit Bridge API Server ═══
            try
            {
                _bridgeHandler = new RevitApiHandler();
                var externalEvent = ExternalEvent.Create(_bridgeHandler);
                _bridgeServer = new RevitBridgeServer(_bridgeHandler, externalEvent);
                _bridgeServer.Start();
                System.Diagnostics.Debug.WriteLine("[CIC Bridge] API Server started on http://localhost:52140/");
            }
            catch (Exception bridgeEx)
            {
                System.Diagnostics.Debug.WriteLine($"[CIC Bridge] Init failed: {bridgeEx.Message}");
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
        // Stop Bridge server
        _bridgeServer?.Dispose();
        _bridgeServer = null;

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

