using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Autodesk.Revit.DB;
using CIC.BIM.Addin.Tools.Services;

namespace CIC.BIM.Addin.Tools.Views;

public partial class FormworkWindow : Window
{
    private readonly Document? _doc;
    private FormworkResult? _result;
    private List<Element>? _selectedElements;
    private bool _hasChanges;

    /// <summary>Kết quả tính toán.</summary>
    public FormworkResult? Result => _result;

    /// <summary>True nếu đã commit transaction (tạo/xóa VK).</summary>
    public bool HasChanges => _hasChanges;

    /// <summary>True nếu user muốn xuất kết quả.</summary>
    public bool ExportRequested { get; private set; }

    /// <summary>True nếu user chọn "Chọn cấu kiện".</summary>
    public bool SelectElements { get; private set; }

    // Options
    public bool IncludeBeam => ChkBeam.IsChecked == true;
    public bool IncludeColumn => ChkColumn.IsChecked == true;
    public bool IncludeWall => ChkWall.IsChecked == true;
    public bool IncludeFloor => ChkFloor.IsChecked == true;
    public bool IncludeFoundation => ChkFoundation.IsChecked == true;

    public bool AutoDeductIntersection => ChkAutoDeduct.IsChecked == true;
    public bool GroupByLevel => ChkGroupByLevel.IsChecked == true;
    public bool GroupByType => ChkGroupByType.IsChecked == true;
    public bool SaveSharedParam => ChkSaveSharedParam.IsChecked == true;
    public bool CreateGeometry => ChkCreateGeometry.IsChecked == true;

    // Xuất mặc định cả 2
    public bool OutputSchedule => true;
    public bool OutputExcel => true;

    public FormworkWindow(Document? doc = null, List<Element>? selectedElements = null)
    {
        InitializeComponent();
        _doc = doc;
        _selectedElements = selectedElements;

        // Hiện số VK hiện có
        UpdateVKCount();
    }

    public FormworkOptions BuildOptions()
    {
        return new FormworkOptions
        {
            IncludeBeam = IncludeBeam,
            IncludeColumn = IncludeColumn,
            IncludeWall = IncludeWall,
            IncludeFloor = IncludeFloor,
            IncludeFoundation = IncludeFoundation,
            AutoDeductIntersection = AutoDeductIntersection,
            GroupByLevel = GroupByLevel,
            GroupByType = GroupByType
        };
    }

    // ═══════════════════════════════════════════
    //  NÚT CHÍNH: Tính & Tạo ván khuôn
    // ═══════════════════════════════════════════

    private void BtnRunAll_Click(object sender, RoutedEventArgs e)
    {
        if (_doc == null)
        {
            ShowMsg("Document không khả dụng.", "Lỗi", MessageBoxImage.Error);
            return;
        }

        BtnRunAll.IsEnabled = false;
        var messages = new List<string>();

        try
        {
            // === BƯỚC 1: THU THẬP CẤU KIỆN ===
            SetStatus("⏳ Bước 1/4: Thu thập cấu kiện...", 10);

            var options = BuildOptions();
            var elements = _selectedElements ?? FormworkService.CollectElements(_doc, options);

            if (elements.Count == 0)
            {
                ShowMsg("Không tìm thấy cấu kiện kết cấu nào.\nKiểm tra checkbox chọn loại cấu kiện.",
                    "Thống kê Ván khuôn", MessageBoxImage.Warning);
                SetStatus("⚠ Không tìm thấy cấu kiện.", 0);
                return;
            }

            messages.Add($"📦 Thu thập: {elements.Count} cấu kiện");

            // === BƯỚC 2: TÍNH DIỆN TÍCH ===
            SetStatus($"⏳ Bước 2/4: Tính diện tích ({elements.Count} CK)...", 30);

            var service = new FormworkService();
            _result = service.Calculate(_doc, elements, options);

            ShowResults(_result);
            messages.Add($"📐 Diện tích ròng: {_result.TotalNetArea:N2} m²");
            messages.Add($"   (Thô: {_result.TotalGrossArea:N2} — Trừ GN: {_result.TotalDeduction:N2})");

            // === BƯỚC 3: TẠO VÁN KHUÔN 3D ===
            if (CreateGeometry)
            {
                SetStatus("⏳ Bước 3/4: Tạo ván khuôn 3D...", 50);

                if (!double.TryParse(TxtThickness.Text, out var thicknessMm) || thicknessMm <= 0)
                    thicknessMm = 18.0;

                var creationResult = FormworkGeometryService.CreateFormwork(_doc, elements, thicknessMm,
                    (current, total) =>
                    {
                        if (total > 0)
                            PrgMain.Value = 50 + (double)current / total * 30;
                    });

                messages.Add($"🏗️ Tạo VK 3D: {creationResult.Created} đối tượng ({creationResult.FacesProcessed} mặt)");

                if (creationResult.Errors.Count > 0)
                {
                    messages.Add($"   ⚠ {creationResult.Errors.Count} cảnh báo:");
                    foreach (var err in creationResult.Errors.Take(3))
                        messages.Add($"     • {err}");
                }
            }
            else
            {
                messages.Add("🏗️ Bỏ qua tạo VK 3D (không chọn)");
            }

            // === BƯỚC 4: GHI SHARED PARAMETER ===
            if (SaveSharedParam && _result.Items.Count > 0)
            {
                SetStatus("⏳ Bước 4/4: Ghi CIC_FormworkArea...", 85);

                try
                {
                    var count = FormworkExportService.WriteSharedParameter(_doc, _result);
                    messages.Add($"💾 Ghi param: {count} cấu kiện");
                }
                catch (Exception ex)
                {
                    messages.Add($"⚠ Lỗi ghi param: {ex.Message}");
                }
            }

            // === HOÀN TẤT ===
            _hasChanges = true;
            SetStatus($"✅ Hoàn tất: {_result.ElementCount} CK | {_result.TotalNetArea:N2} m²", 100);
            BtnExport.IsEnabled = true;
            UpdateVKCount();

            // Hiện kết quả tổng hợp
            ShowMsg(string.Join("\n", messages),
                "✅ Hoàn tất thống kê ván khuôn", MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            SetStatus($"❌ Lỗi: {ex.Message}", 0);
            ShowMsg($"Lỗi:\n{ex.Message}\n\nStack:\n{ex.StackTrace?.Substring(0, Math.Min(400, ex.StackTrace?.Length ?? 0))}",
                "Lỗi nghiêm trọng", MessageBoxImage.Error);
        }
        finally
        {
            BtnRunAll.IsEnabled = true;
        }
    }

    // ═══════════════════════════════════════════
    //  HIỂN THỊ KẾT QUẢ
    // ═══════════════════════════════════════════

    private void ShowResults(FormworkResult result)
    {
        TxtResultHeader.Visibility = System.Windows.Visibility.Visible;
        ResultPanel.Visibility = System.Windows.Visibility.Visible;

        var displayItems = result.Items
            .OrderBy(i => i.LevelName)
            .ThenBy(i => i.Category)
            .ThenBy(i => i.TypeName)
            .ToList();

        DgResults.ItemsSource = displayItems;

        TxtSummary.Text = $"{result.ElementCount} cấu kiện  |  Trừ GN: {result.TotalDeduction:N2} m²";
        TxtTotal.Text = $"Tổng: {result.TotalNetArea:N2} m²";
    }

    // ═══════════════════════════════════════════
    //  BUTTON HANDLERS
    // ═══════════════════════════════════════════

    private void BtnSelectCalc_Click(object sender, RoutedEventArgs e)
    {
        SelectElements = true;
        DialogResult = true;
        Close();
    }

    private void BtnExport_Click(object sender, RoutedEventArgs e)
    {
        if (_result == null || _result.Items.Count == 0)
        {
            ShowMsg("Chưa có kết quả. Bấm '▶ Tính & Tạo' trước.",
                "Xuất kết quả", MessageBoxImage.Warning);
            return;
        }

        ExportRequested = true;
        DialogResult = true;
        Close();
    }

    private void BtnDeleteFormwork_Click(object sender, RoutedEventArgs e)
    {
        if (_doc == null)
        {
            ShowMsg("Document không khả dụng.", "Lỗi", MessageBoxImage.Error);
            return;
        }

        try
        {
            var count = FormworkGeometryService.CountExisting(_doc);
            if (count == 0)
            {
                ShowMsg("Không có ván khuôn nào đã tạo để xóa.", "Xóa ván khuôn", MessageBoxImage.Information);
                return;
            }

            var answer = MessageBox.Show(
                $"Xóa {count} đối tượng ván khuôn đã tạo trước đó?",
                "Xác nhận xóa", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (answer != MessageBoxResult.Yes) return;

            var deleted = FormworkGeometryService.DeleteAll(_doc);
            _hasChanges = true;
            SetStatus($"🗑️ Đã xóa {deleted} đối tượng ván khuôn.", 0);
            UpdateVKCount();
            ShowMsg($"Đã xóa {deleted} đối tượng ván khuôn.", "✅ Xóa thành công", MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            SetStatus($"❌ Lỗi xóa: {ex.Message}", 0);
            ShowMsg($"Lỗi khi xóa:\n{ex.Message}", "Lỗi", MessageBoxImage.Error);
        }
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        // Nếu đã có thay đổi (tạo/xóa VK), trả true để Revit KHÔNG undo
        DialogResult = _hasChanges;
        Close();
    }

    // ═══════════════════════════════════════════
    //  HELPERS
    // ═══════════════════════════════════════════

    private void SetStatus(string text, double progress)
    {
        TxtStatus.Text = text;
        PrgMain.Value = progress;
    }

    private void UpdateVKCount()
    {
        if (_doc == null) return;
        try
        {
            var count = FormworkGeometryService.CountExisting(_doc);
            BtnDeleteVK.Content = count > 0
                ? $"🗑️ Xóa VK ({count})"
                : "🗑️ Xóa VK";
        }
        catch { }
    }

    private static void ShowMsg(string msg, string title, MessageBoxImage icon)
    {
        MessageBox.Show(msg, title, MessageBoxButton.OK, icon);
    }
}
