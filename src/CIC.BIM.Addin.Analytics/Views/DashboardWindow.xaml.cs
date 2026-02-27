using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using CIC.BIM.Addin.Analytics.Models;
using CIC.BIM.Addin.Analytics.Services;

namespace CIC.BIM.Addin.Analytics.Views;

public partial class DashboardWindow : Window
{
    private readonly LocalStore _store;

    // Catppuccin Mocha palette
    private static readonly Color ColorModeling = Color.FromRgb(0xA6, 0xE3, 0xA1);   // Xanh lá
    private static readonly Color ColorEditing = Color.FromRgb(0x89, 0xB4, 0xFA);    // Xanh dương
    private static readonly Color ColorViewing = Color.FromRgb(0xF9, 0xE2, 0xAF);    // Vàng
    private static readonly Color ColorDocumenting = Color.FromRgb(0xCB, 0xA6, 0xF7); // Tím
    private static readonly Color ColorFileOps = Color.FromRgb(0x94, 0xE2, 0xD5);    // Teal
    private static readonly Color ColorCoordinating = Color.FromRgb(0x74, 0xC7, 0xEC); // Sapphire
    private static readonly Color ColorIdle = Color.FromRgb(0xF3, 0x8B, 0xA8);       // Đỏ
    private static readonly Color ColorDialogWait = Color.FromRgb(0x6C, 0x70, 0x86); // Xám

    private DateTime _selectedDate;

    public DashboardWindow(LocalStore store)
    {
        InitializeComponent();
        _store = store;
        _selectedDate = DateTime.Now.Date;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        DatePickerControl.SelectedDate = _selectedDate;
        UserLabel.Text = $"👤 {Environment.UserName} @ {Environment.MachineName}";
        LoadDashboardData();
    }

    private void DatePickerControl_SelectedDateChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DatePickerControl.SelectedDate.HasValue)
        {
            _selectedDate = DatePickerControl.SelectedDate.Value.Date;
            LoadDashboardData();
        }
    }

    private void LoadDashboardData()
    {
        try
        {
            DateLabel.Text = _selectedDate.ToString("dd/MM/yyyy (dddd)");
            var summary = _store.GetDailySummary(_selectedDate);

            if (summary == null || summary.TotalActiveMinutes == 0)
            {
                ResetAllKPIs();
                return;
            }

            // ═══ KPI Hàng 1: Thời gian ═══
            var totalHours = (int)(summary.TotalActiveMinutes / 60);
            var totalMins = (int)(summary.TotalActiveMinutes % 60);
            TotalActiveText.Text = $"{totalHours} giờ {totalMins} phút";
            ModelingText.Text = FormatMinutes(summary.ModelingMinutes);
            EditingText.Text = FormatMinutes(summary.EditingMinutes);
            DocumentingText.Text = FormatMinutes(summary.DocumentingMinutes);
            CoordinatingText.Text = FormatMinutes(summary.CoordinatingMinutes);

            // ═══ KPI Hàng 2: Hiệu suất ═══
            IdleText.Text = FormatMinutes(summary.IdleMinutes);
            DialogWaitText.Text = FormatMinutes(summary.DialogWaitMinutes);
            SessionsText.Text = summary.TotalSessions.ToString();

            // Điểm hiệu suất: Thời gian sản xuất / Tổng thời gian
            var productiveMinutes = summary.ModelingMinutes + summary.EditingMinutes 
                + summary.DocumentingMinutes + summary.CoordinatingMinutes;
            if (summary.TotalActiveMinutes > 0)
            {
                var efficiency = productiveMinutes / summary.TotalActiveMinutes * 100;
                EfficiencyText.Text = $"{efficiency:F0}%";
                EfficiencyText.Foreground = new SolidColorBrush(
                    efficiency >= 70 ? ColorModeling :
                    efficiency >= 40 ? Color.FromRgb(0xF9, 0xE2, 0xAF) :
                    ColorIdle);
            }
            else
            {
                EfficiencyText.Text = "--";
            }

            // Năng suất EPR (Elements Per Active Hour)
            if (summary.ElementsPerActiveHour > 0)
            {
                EPRText.Text = $"{summary.ElementsPerActiveHour:F0}";
                EPRText.Foreground = new SolidColorBrush(
                    summary.ElementsPerActiveHour >= 50 ? ColorModeling :
                    summary.ElementsPerActiveHour >= 20 ? Color.FromRgb(0xFA, 0xB3, 0x87) :
                    ColorIdle);
            }
            else
            {
                EPRText.Text = "--";
            }

            // ═══ Số lượng Element ═══
            ElementsCreatedText.Text = summary.ElementsCreated.ToString("N0");
            ElementsModifiedText.Text = summary.ElementsModified.ToString("N0");
            ElementsDeletedText.Text = summary.ElementsDeleted.ToString("N0");

            // ═══ Biểu đồ ═══
            DrawActivityBars(summary);
            DrawWeeklyChart();

            // ═══ Phát hiện ═══
            GenerateWarnings(summary);
        }
        catch (Exception ex)
        {
            WarningsText.Text = $"Lỗi tải dữ liệu: {ex.Message}";
        }
    }

    private void ResetAllKPIs()
    {
        TotalActiveText.Text = "0 giờ 0 phút";
        ModelingText.Text = "0 phút";
        EditingText.Text = "0 phút";
        DocumentingText.Text = "0 phút";
        CoordinatingText.Text = "0 phút";
        IdleText.Text = "0 phút";
        DialogWaitText.Text = "0 phút";
        SessionsText.Text = "0";
        EfficiencyText.Text = "--";
        EPRText.Text = "--";
        ElementsCreatedText.Text = "0";
        ElementsModifiedText.Text = "0";
        ElementsDeletedText.Text = "0";
        ActivityBarsPanel.Children.Clear();
        WeeklyChartCanvas.Children.Clear();

        var isToday = _selectedDate.Date == DateTime.Now.Date;
        WarningsText.Text = isToday
            ? "Chưa có dữ liệu cho hôm nay. Hãy bắt đầu làm việc trong Revit!"
            : $"Không có dữ liệu cho ngày {_selectedDate:dd/MM/yyyy}.";
    }

    private void DrawActivityBars(DailySummary summary)
    {
        ActivityBarsPanel.Children.Clear();

        var totalMinutes = summary.TotalActiveMinutes + summary.IdleMinutes + summary.DialogWaitMinutes;
        if (totalMinutes <= 0) return;

        var items = new[]
        {
            ("🏗 Dựng mô hình", summary.ModelingMinutes, ColorModeling),
            ("✏️ Chỉnh sửa", summary.EditingMinutes, ColorEditing),
            ("👁 Xem/Điều hướng", summary.ViewingMinutes, ColorViewing),
            ("📄 Xuất bản vẽ", summary.DocumentingMinutes, ColorDocumenting),
            ("📁 Thao tác tập tin", summary.FileOpsMinutes, ColorFileOps),
            ("🔗 Phối hợp", summary.CoordinatingMinutes, ColorCoordinating),
            ("⏳ Chờ hộp thoại", summary.DialogWaitMinutes, ColorDialogWait),
            ("😴 Chờ / Nghỉ", summary.IdleMinutes, ColorIdle),
        };

        foreach (var (label, minutes, color) in items)
        {
            if (minutes <= 0) continue;

            var percentage = minutes / totalMinutes;
            var panel = new StackPanel { Margin = new Thickness(0, 3, 0, 3) };

            // Nhãn
            var labelRow = new DockPanel();
            labelRow.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 12,
                Foreground = new SolidColorBrush(color)
            });
            labelRow.Children.Add(new TextBlock
            {
                Text = $"{FormatMinutes(minutes)} ({percentage:P0})",
                FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Right,
                Foreground = new SolidColorBrush(Color.FromRgb(0xA6, 0xAD, 0xC8))
            });
            DockPanel.SetDock(labelRow.Children[1], Dock.Right);
            panel.Children.Add(labelRow);

            // Thanh tiến trình
            var barBg = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x45, 0x47, 0x5A)),
                CornerRadius = new CornerRadius(3),
                Height = 8,
                Margin = new Thickness(0, 2, 0, 0)
            };
            var barFill = new Border
            {
                Background = new SolidColorBrush(color),
                CornerRadius = new CornerRadius(3),
                Height = 8,
                HorizontalAlignment = HorizontalAlignment.Left,
                Width = Math.Max(4, 300 * percentage)
            };
            var barGrid = new Grid();
            barGrid.Children.Add(barBg);
            barGrid.Children.Add(barFill);
            panel.Children.Add(barGrid);

            ActivityBarsPanel.Children.Add(panel);
        }
    }

    private void DrawWeeklyChart()
    {
        WeeklyChartCanvas.Children.Clear();

        var today = DateTime.Now.Date;
        // Đầu tuần (Thứ Hai)
        var dayOfWeek = (int)_selectedDate.DayOfWeek;
        if (dayOfWeek == 0) dayOfWeek = 7; // Chủ nhật = 7
        var weekStart = _selectedDate.AddDays(-(dayOfWeek - 1));

        var summaries = _store.GetWeeklySummaries(weekStart);
        if (summaries.Count == 0) return;

        var maxMinutes = summaries.Max(s => s.TotalActiveMinutes + s.IdleMinutes);
        if (maxMinutes <= 0) maxMinutes = 1;

        var canvasWidth = 350.0;
        var canvasHeight = 260.0;
        var barWidth = canvasWidth / 7 * 0.6;
        var barSpacing = canvasWidth / 7;
        var dayNames = new[] { "T2", "T3", "T4", "T5", "T6", "T7", "CN" };

        for (int i = 0; i < 7; i++)
        {
            var date = weekStart.AddDays(i);
            var summary = summaries.FirstOrDefault(s => s.SummaryDate.Date == date.Date);
            var totalMinutes = summary != null ? summary.TotalActiveMinutes : 0;
            var idleMinutes = summary != null ? summary.IdleMinutes : 0;
            var isSelected = date.Date == _selectedDate.Date;

            var x = i * barSpacing + (barSpacing - barWidth) / 2;

            // Thanh thời gian làm việc
            var activeHeight = Math.Max(0, (totalMinutes / maxMinutes) * (canvasHeight - 30));
            var activeBar = new Rectangle
            {
                Width = barWidth,
                Height = activeHeight,
                Fill = new SolidColorBrush(isSelected ? ColorModeling : Color.FromRgb(0x89, 0xB4, 0xFA)),
                RadiusX = 4,
                RadiusY = 4,
                Opacity = isSelected ? 1.0 : 0.7
            };
            Canvas.SetLeft(activeBar, x);
            Canvas.SetTop(activeBar, canvasHeight - 30 - activeHeight);
            WeeklyChartCanvas.Children.Add(activeBar);

            // Thanh idle (xếp chồng)
            if (idleMinutes > 0)
            {
                var idleHeight = Math.Max(0, (idleMinutes / maxMinutes) * (canvasHeight - 30));
                var idleBar = new Rectangle
                {
                    Width = barWidth,
                    Height = idleHeight,
                    Fill = new SolidColorBrush(ColorIdle),
                    RadiusX = 4,
                    RadiusY = 4,
                    Opacity = 0.5
                };
                Canvas.SetLeft(idleBar, x);
                Canvas.SetTop(idleBar, canvasHeight - 30 - activeHeight - idleHeight);
                WeeklyChartCanvas.Children.Add(idleBar);
            }

            // Nhãn ngày
            var label = new TextBlock
            {
                Text = dayNames[i],
                FontSize = 11,
                Foreground = new SolidColorBrush(isSelected
                    ? Color.FromRgb(0xF5, 0xE0, 0xDC)
                    : Color.FromRgb(0x6C, 0x70, 0x86)),
                FontWeight = isSelected ? FontWeights.Bold : FontWeights.Normal
            };
            Canvas.SetLeft(label, x + barWidth / 2 - 8);
            Canvas.SetTop(label, canvasHeight - 20);
            WeeklyChartCanvas.Children.Add(label);

            // Nhãn thời gian trên đầu cột
            if (totalMinutes > 0)
            {
                var minuteLabel = new TextBlock
                {
                    Text = FormatMinutes(totalMinutes),
                    FontSize = 9,
                    Foreground = new SolidColorBrush(Color.FromRgb(0xA6, 0xAD, 0xC8))
                };
                Canvas.SetLeft(minuteLabel, x);
                Canvas.SetTop(minuteLabel, canvasHeight - 30 - activeHeight - (idleMinutes > 0 ? 25 : 15));
                WeeklyChartCanvas.Children.Add(minuteLabel);
            }
        }
    }

    private void GenerateWarnings(DailySummary summary)
    {
        var warnings = new List<string>();

        var totalTime = summary.TotalActiveMinutes + summary.IdleMinutes;
        if (totalTime > 0)
        {
            var idleRatio = summary.IdleMinutes / totalTime;
            if (idleRatio > 0.3)
                warnings.Add($"⚠️ Tỷ lệ nghỉ/chờ cao ({idleRatio:P0}) — kiểm tra quy trình hoặc thiết lập mô hình");

            if (summary.ModelingMinutes > 0 && summary.EditingMinutes / summary.ModelingMinutes > 2)
                warnings.Add("⚠️ Thời gian chỉnh sửa gấp đôi thời gian dựng mô hình — có thể đang làm lại nhiều");

            if (summary.ViewingMinutes > summary.ModelingMinutes && summary.ModelingMinutes > 0)
                warnings.Add("💡 Thời gian xem mô hình > thời gian dựng — cân nhắc dùng tìm kiếm hoặc bộ lọc nhanh");

            if (summary.DialogWaitMinutes > 30)
                warnings.Add("⚠️ Chờ hộp thoại/cảnh báo Revit > 30 phút — kiểm tra cài đặt hoặc mô hình nặng");

            // Nhận xét năng suất element
            if (summary.ElementsPerActiveHour >= 50)
                warnings.Add($"🏆 Năng suất xuất sắc: {summary.ElementsPerActiveHour:F0} phần tử/giờ");
            else if (summary.ElementsPerActiveHour >= 20)
                warnings.Add($"👍 Năng suất khá: {summary.ElementsPerActiveHour:F0} phần tử/giờ");
            else if (summary.ElementsPerActiveHour > 0 && summary.TotalActiveMinutes > 30)
                warnings.Add($"📝 Năng suất thấp: {summary.ElementsPerActiveHour:F0} phần tử/giờ — kiểm tra quy trình làm việc");
        }

        if (warnings.Count == 0)
            warnings.Add("✅ Hiệu suất tốt! Không phát hiện vấn đề.");

        WarningsText.Text = string.Join("\n", warnings);
    }

    private static string FormatMinutes(double minutes)
    {
        if (minutes < 1) return $"{minutes * 60:F0} giây";
        if (minutes < 60) return $"{minutes:F0} phút";
        return $"{(int)(minutes / 60)} giờ {(int)(minutes % 60)} phút";
    }
}
