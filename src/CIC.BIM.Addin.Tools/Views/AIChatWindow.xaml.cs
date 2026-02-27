using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Autodesk.Revit.DB;
using CIC.BIM.Addin.Tools.Services;

namespace CIC.BIM.Addin.Tools.Views;

public partial class AIChatWindow : Window
{
    private readonly Document _doc;
    private readonly AIChatService _chatService;
    private bool _isProcessing;

    public AIChatWindow(Document doc)
    {
        InitializeComponent();
        _doc = doc;
        _chatService = new AIChatService();

        // Load model context on startup
        Loaded += async (s, e) => await LoadModelContext();

        // Focus input
        Loaded += (s, e) => txtInput.Focus();
    }

    #region Event Handlers

    private async void BtnSend_Click(object sender, RoutedEventArgs e)
    {
        await SendMessage();
    }

    private async void TxtInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !_isProcessing)
        {
            e.Handled = true;
            await SendMessage();
        }
    }

    private async void BtnRefresh_Click(object sender, RoutedEventArgs e)
    {
        await LoadModelContext();
    }

    private void BtnSettings_Click(object sender, RoutedEventArgs e)
    {
        ShowApiKeyDialog();
    }

    private void BtnClear_Click(object sender, RoutedEventArgs e)
    {
        // Keep welcome message (first child)
        while (chatPanel.Children.Count > 1)
            chatPanel.Children.RemoveAt(chatPanel.Children.Count - 1);

        _chatService.ClearHistory();
        SetStatus("Đã xóa lịch sử chat", "#A6E3A1");
    }

    #endregion

    #region Core Logic

    private async Task SendMessage()
    {
        var question = txtInput.Text?.Trim();
        if (string.IsNullOrEmpty(question) || _isProcessing) return;

        // Add user bubble
        AddChatBubble(question, isUser: true);
        txtInput.Text = "";
        txtInput.Focus();

        // Show typing indicator
        _isProcessing = true;
        btnSend.IsEnabled = false;
        SetStatus("AI đang suy nghĩ...", "#FAB387");
        var typingBubble = AddTypingIndicator();

        try
        {
            // Call AI
            var result = await _chatService.AskAsync(question);

            // Remove typing indicator, add AI response
            chatPanel.Children.Remove(typingBubble);
            AddChatBubble(result, isUser: false);
            SetStatus("Sẵn sàng", "#A6E3A1");
        }
        catch (Exception ex)
        {
            chatPanel.Children.Remove(typingBubble);
            AddChatBubble($"⚠️ Lỗi: {ex.Message}", isUser: false);
            SetStatus("Lỗi", "#F38BA8");
        }
        finally
        {
            _isProcessing = false;
            btnSend.IsEnabled = true;
        }
    }

    private async Task LoadModelContext()
    {
        SetStatus("Đang tải dữ liệu model...", "#FAB387");
        btnRefresh.IsEnabled = false;

        try
        {
            // ExtractModelContext must run on Revit's thread (current thread)
            var context = ModelExtractorService.ExtractModelContext(_doc);
            _chatService.SetModelContext(context);

            txtProjectName.Text = $"📁 {_doc.Title}";
            SetStatus($"Đã tải model — sẵn sàng hỏi đáp", "#A6E3A1");

            // Show context size info
            var lines = context.Split('\n').Length;
            var chars = context.Length;
            System.Diagnostics.Debug.WriteLine($"[CIC AI] Context: {lines} lines, {chars} chars");
        }
        catch (Exception ex)
        {
            SetStatus($"Lỗi tải model: {ex.Message}", "#F38BA8");
        }
        finally
        {
            btnRefresh.IsEnabled = true;
        }
    }

    #endregion

    #region UI Helpers

    private void AddChatBubble(string text, bool isUser)
    {
        var bubble = new Border
        {
            Background = new SolidColorBrush(isUser
                ? ColorFromHex("#45475A")
                : ColorFromHex("#313244")),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(14, 10, 14, 10),
            Margin = isUser
                ? new Thickness(40, 4, 0, 4)
                : new Thickness(0, 4, 40, 4),
            HorizontalAlignment = isUser
                ? HorizontalAlignment.Right
                : HorizontalAlignment.Left,
            MaxWidth = 400
        };

        var textBlock = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13,
            FontFamily = new FontFamily("Segoe UI"),
            Foreground = new SolidColorBrush(ColorFromHex("#CDD6F4"))
        };

        if (isUser)
        {
            textBlock.Text = text;
        }
        else
        {
            // AI message: add role label
            var roleRun = new Run("🤖 AI")
            {
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(ColorFromHex("#89B4FA")),
                FontSize = 11.5
            };
            textBlock.Inlines.Add(roleRun);
            textBlock.Inlines.Add(new LineBreak());
            textBlock.Inlines.Add(new Run(text));
        }

        bubble.Child = textBlock;
        chatPanel.Children.Add(bubble);

        // Auto-scroll to bottom
        chatScroller.ScrollToEnd();
    }

    private Border AddTypingIndicator()
    {
        var bubble = new Border
        {
            Background = new SolidColorBrush(ColorFromHex("#313244")),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(14, 10, 14, 10),
            Margin = new Thickness(0, 4, 40, 4),
            HorizontalAlignment = HorizontalAlignment.Left,
            MaxWidth = 200
        };

        var textBlock = new TextBlock
        {
            Text = "🤖 Đang suy nghĩ...",
            FontSize = 12,
            FontStyle = FontStyles.Italic,
            Foreground = new SolidColorBrush(ColorFromHex("#6C7086"))
        };

        bubble.Child = textBlock;
        chatPanel.Children.Add(bubble);
        chatScroller.ScrollToEnd();

        return bubble;
    }

    private void SetStatus(string text, string colorHex)
    {
        txtStatus.Text = text;
        statusDot.Fill = new SolidColorBrush(ColorFromHex(colorHex));
    }

    private void ShowApiKeyDialog()
    {
        var dialog = new Window
        {
            Title = "⚙️ Cài đặt API Key",
            Width = 460,
            Height = 260,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            Background = new SolidColorBrush(ColorFromHex("#1E1E2E")),
            ResizeMode = ResizeMode.NoResize
        };

        var panel = new StackPanel { Margin = new Thickness(20) };

        var label = new TextBlock
        {
            Text = "Google Gemini API Key",
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(ColorFromHex("#CDD6F4")),
            Margin = new Thickness(0, 0, 0, 8)
        };
        panel.Children.Add(label);

        var desc = new TextBlock
        {
            Text = "Lấy API key miễn phí tại: https://aistudio.google.com/apikey",
            FontSize = 11,
            Foreground = new SolidColorBrush(ColorFromHex("#A6ADC8")),
            Margin = new Thickness(0, 0, 0, 12),
            TextWrapping = TextWrapping.Wrap
        };
        panel.Children.Add(desc);

        var input = new TextBox
        {
            Background = new SolidColorBrush(ColorFromHex("#45475A")),
            Foreground = new SolidColorBrush(ColorFromHex("#CDD6F4")),
            BorderBrush = new SolidColorBrush(ColorFromHex("#585B70")),
            BorderThickness = new Thickness(1),
            FontSize = 13,
            Padding = new Thickness(10, 8, 10, 8),
            CaretBrush = new SolidColorBrush(ColorFromHex("#CDD6F4")),
            Text = _chatService.HasApiKey ? "••••••••••••••••••••" : "",
            Margin = new Thickness(0, 0, 0, 16)
        };
        input.GotFocus += (s, e) =>
        {
            if (input.Text.StartsWith("••"))
                input.Text = "";
        };
        panel.Children.Add(input);

        var btnPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var btnCancel = new Button
        {
            Content = "Hủy",
            Width = 80,
            Height = 32,
            Background = new SolidColorBrush(ColorFromHex("#45475A")),
            Foreground = new SolidColorBrush(ColorFromHex("#CDD6F4")),
            BorderThickness = new Thickness(0),
            FontSize = 13,
            Cursor = Cursors.Hand,
            Margin = new Thickness(0, 0, 8, 0)
        };
        btnCancel.Click += (s, e) => dialog.Close();
        btnPanel.Children.Add(btnCancel);

        var btnSave = new Button
        {
            Content = "Lưu",
            Width = 80,
            Height = 32,
            Background = new SolidColorBrush(ColorFromHex("#89B4FA")),
            Foreground = new SolidColorBrush(ColorFromHex("#1E1E2E")),
            BorderThickness = new Thickness(0),
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Cursor = Cursors.Hand
        };
        btnSave.Click += (s, e) =>
        {
            var key = input.Text?.Trim();
            if (!string.IsNullOrEmpty(key) && !key.StartsWith("••"))
            {
                _chatService.SetApiKey(key);
                SetStatus("Đã lưu API Key ✓", "#A6E3A1");
            }
            dialog.Close();
        };
        btnPanel.Children.Add(btnSave);

        panel.Children.Add(btnPanel);
        dialog.Content = panel;
        dialog.ShowDialog();
    }

    private static System.Windows.Media.Color ColorFromHex(string hex)
    {
        hex = hex.TrimStart('#');
        var r = Convert.ToByte(hex.Substring(0, 2), 16);
        var g = Convert.ToByte(hex.Substring(2, 2), 16);
        var b = Convert.ToByte(hex.Substring(4, 2), 16);
        return System.Windows.Media.Color.FromRgb(r, g, b);
    }

    #endregion

    protected override void OnClosed(EventArgs e)
    {
        _chatService?.Dispose();
        base.OnClosed(e);
    }
}
