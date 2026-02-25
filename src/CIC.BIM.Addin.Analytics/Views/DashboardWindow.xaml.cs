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
    private static readonly Color ColorModeling = Color.FromRgb(0xA6, 0xE3, 0xA1);   // Green
    private static readonly Color ColorEditing = Color.FromRgb(0x89, 0xB4, 0xFA);    // Blue
    private static readonly Color ColorViewing = Color.FromRgb(0xF9, 0xE2, 0xAF);    // Yellow
    private static readonly Color ColorDocumenting = Color.FromRgb(0xCB, 0xA6, 0xF7); // Mauve
    private static readonly Color ColorFileOps = Color.FromRgb(0x94, 0xE2, 0xD5);    // Teal
    private static readonly Color ColorIdle = Color.FromRgb(0xF3, 0x8B, 0xA8);       // Red
    private static readonly Color ColorDialogWait = Color.FromRgb(0x6C, 0x70, 0x86); // Overlay

    public DashboardWindow(LocalStore store)
    {
        InitializeComponent();
        _store = store;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        DateLabel.Text = DateTime.Now.ToString("dd/MM/yyyy (dddd)");
        UserLabel.Text = $"👤 {Environment.UserName} @ {Environment.MachineName}";

        LoadDashboardData();
    }

    private void LoadDashboardData()
    {
        try
        {
            var today = DateTime.Now.Date;
            var summary = _store.GetDailySummary(today);

            if (summary == null || summary.TotalActiveMinutes == 0)
            {
                TotalActiveText.Text = "0h 0m";
                ModelingText.Text = "0m";
                EditingText.Text = "0m";
                IdleText.Text = "0m";
                SessionsText.Text = "0";
                WarningsText.Text = "Chưa có dữ liệu cho hôm nay. Hãy bắt đầu làm việc!";
                return;
            }

            // KPI Cards
            var totalHours = (int)(summary.TotalActiveMinutes / 60);
            var totalMins = (int)(summary.TotalActiveMinutes % 60);
            TotalActiveText.Text = $"{totalHours}h {totalMins}m";
            ModelingText.Text = FormatMinutes(summary.ModelingMinutes);
            EditingText.Text = FormatMinutes(summary.EditingMinutes);
            IdleText.Text = FormatMinutes(summary.IdleMinutes);
            SessionsText.Text = summary.TotalSessions.ToString();

            // Activity bars
            DrawActivityBars(summary);

            // Weekly chart
            DrawWeeklyChart();

            // Warnings
            GenerateWarnings(summary);
        }
        catch (Exception ex)
        {
            WarningsText.Text = $"Lỗi tải dữ liệu: {ex.Message}";
        }
    }

    private void DrawActivityBars(DailySummary summary)
    {
        ActivityBarsPanel.Children.Clear();

        var totalMinutes = summary.TotalActiveMinutes + summary.IdleMinutes;
        if (totalMinutes <= 0) return;

        var items = new[]
        {
            ("🏗 Modeling", summary.ModelingMinutes, ColorModeling),
            ("✏️ Editing", summary.EditingMinutes, ColorEditing),
            ("👁 Viewing", summary.ViewingMinutes, ColorViewing),
            ("📄 Documenting", summary.DocumentingMinutes, ColorDocumenting),
            ("📁 File Ops", summary.FileOpsMinutes, ColorFileOps),
            ("😴 Idle", summary.IdleMinutes, ColorIdle),
        };

        foreach (var (label, minutes, color) in items)
        {
            if (minutes <= 0) continue;

            var percentage = minutes / totalMinutes;
            var panel = new StackPanel { Margin = new Thickness(0, 4, 0, 4) };

            // Label row
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

            // Bar
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
                Width = Math.Max(4, 320 * percentage)
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
        // Start of current week (Monday)
        var dayOfWeek = (int)today.DayOfWeek;
        if (dayOfWeek == 0) dayOfWeek = 7; // Sunday = 7
        var weekStart = today.AddDays(-(dayOfWeek - 1));

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

            var x = i * barSpacing + (barSpacing - barWidth) / 2;

            // Active bar
            var activeHeight = Math.Max(0, (totalMinutes / maxMinutes) * (canvasHeight - 30));
            var activeBar = new Rectangle
            {
                Width = barWidth,
                Height = activeHeight,
                Fill = new SolidColorBrush(date.Date == today ? ColorModeling : Color.FromRgb(0x89, 0xB4, 0xFA)),
                RadiusX = 4,
                RadiusY = 4,
                Opacity = date.Date == today ? 1.0 : 0.7
            };
            Canvas.SetLeft(activeBar, x);
            Canvas.SetTop(activeBar, canvasHeight - 30 - activeHeight);
            WeeklyChartCanvas.Children.Add(activeBar);

            // Idle bar (stacked on top)
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

            // Day label
            var label = new TextBlock
            {
                Text = dayNames[i],
                FontSize = 11,
                Foreground = new SolidColorBrush(date.Date == today
                    ? Color.FromRgb(0xF5, 0xE0, 0xDC)
                    : Color.FromRgb(0x6C, 0x70, 0x86)),
                FontWeight = date.Date == today ? FontWeights.Bold : FontWeights.Normal
            };
            Canvas.SetLeft(label, x + barWidth / 2 - 8);
            Canvas.SetTop(label, canvasHeight - 20);
            WeeklyChartCanvas.Children.Add(label);

            // Minutes label on top of bar
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
                warnings.Add($"⚠️ Tỷ lệ idle cao ({idleRatio:P0}) — kiểm tra workflow hoặc tối ưu thiết lập model");

            if (summary.ModelingMinutes > 0 && summary.EditingMinutes / summary.ModelingMinutes > 2)
                warnings.Add("⚠️ Thời gian chỉnh sửa gấp đôi modeling — có thể đang rework nhiều");

            if (summary.ViewingMinutes > summary.ModelingMinutes && summary.ModelingMinutes > 0)
                warnings.Add("💡 Thời gian xem view > modeling — cân nhắc sử dụng search hoặc quick filter");
        }

        if (warnings.Count == 0)
            warnings.Add("✅ Hiệu suất tốt! Không phát hiện vấn đề.");

        WarningsText.Text = string.Join("\n", warnings);
    }

    private static string FormatMinutes(double minutes)
    {
        if (minutes < 1) return $"{minutes * 60:F0}s";
        if (minutes < 60) return $"{minutes:F0}m";
        return $"{(int)(minutes / 60)}h {(int)(minutes % 60)}m";
    }
}
