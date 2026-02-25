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
    private const string TabName = "CIC Tool";
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

            // ═══ Panel: Quản lý Vận hành ═══
            var panelFM = application.CreateRibbonPanel(TabName, PanelFM);

            var assemblyPath = Assembly.GetExecutingAssembly().Location;
            var fmAssemblyPath = Path.Combine(
                Path.GetDirectoryName(assemblyPath)!,
                "CIC.BIM.Addin.FacilityMgmt.dll"
            );

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
                    "vào các categories thiết bị MEP trong model."
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
                    "• Sinh mã tài sản AssetCode theo format chuẩn"
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
                    "Chu kỳ bảo trì, Trạng thái, Tình trạng."
            };
            panelFM.AddItem(btnExport);

            // ═══ Analytics: Silent tracking for all users ═══
            // Wrapped in try-catch so FM module still works if Analytics fails
            try
            {
                _activityTracker = new ActivityTracker();
                _activityTracker.StartTracking(application);
                DashboardCommand.Tracker = _activityTracker;

                // ═══ Panel: Analytics Dashboard (admin only) ═══
                if (SupabaseSyncService.IsCurrentUserAdmin())
                {
                    var panelAnalytics = application.CreateRibbonPanel(TabName, PanelAnalytics);
                    var analyticsAssemblyPath = Path.Combine(
                        Path.GetDirectoryName(assemblyPath)!,
                        "CIC.BIM.Addin.Analytics.dll"
                    );

                    var btnDashboard = new PushButtonData(
                        "AnalyticsDashboard",
                        "📊 Dashboard\nHiệu suất",
                        analyticsAssemblyPath,
                        "CIC.BIM.Addin.Analytics.Commands.DashboardCommand"
                    )
                    {
                        ToolTip = "Xem dashboard phân tích hiệu suất làm việc trong Revit",
                        LongDescription = "Hiển thị biểu đồ phân bổ thời gian (Modeling, Editing, Viewing, Idle...),\n" +
                            "phân tích workflow, phát hiện lãng phí quy trình.\n" +
                            "Chỉ hiển thị cho quản trị viên."
                    };
                    panelAnalytics.AddItem(btnDashboard);
                }
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
                        dynamic theme = Activator.CreateInstance(tabThemeType)!;

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
                        TrySet(() => theme.TabHeaderBackground = skyBlue);
                        TrySet(() => theme.TabHeaderForeground = white);
                        TrySet(() => theme.PanelTitleBarBackground = darkBlue);
                        TrySet(() => theme.PanelBackground = lightBlue);
                        TrySet(() => theme.RibbonTabBackground = skyBlue);
                        TrySet(() => theme.ActiveTabBackground = skyBlue);
                        TrySet(() => theme.InactiveTabBackground = skyBlue);

                        // Apply using reflection to bypass type checking
                        var themeProp = tab.GetType().GetProperty("Theme",
                            BindingFlags.Public | BindingFlags.Instance);
                        if (themeProp != null)
                        {
                            themeProp.SetValue(tab, (object)theme);
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

    private static void TrySet(Action action)
    {
        try { action(); } catch { }
    }
}
