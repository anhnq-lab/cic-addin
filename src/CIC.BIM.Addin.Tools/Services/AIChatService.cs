using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace CIC.BIM.Addin.Tools.Services;

/// <summary>
/// Service gọi Google Gemini API để trả lời câu hỏi về mô hình BIM.
/// Hỗ trợ Gemini 2.0 Flash (free tier: 15 RPM, 1M tokens context).
/// </summary>
public class AIChatService : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly List<ChatMessage> _history = new();
    private string _modelContext = "";
    private string _apiKey = "";

    // Gemini API endpoint
    private const string GeminiBaseUrl = "https://generativelanguage.googleapis.com/v1beta/models/gemini-2.0-flash:generateContent";

    private const string SystemPrompt = @"Bạn là trợ lý AI chuyên về BIM (Building Information Modeling) cho dự án xây dựng.
Bạn có quyền truy cập vào dữ liệu mô hình Revit được cung cấp bên dưới.

NGUYÊN TẮC:
- Trả lời bằng tiếng Việt, ngắn gọn, chính xác
- Chỉ trả lời dựa trên dữ liệu được cung cấp, KHÔNG bịa số liệu
- Nếu không tìm thấy thông tin, nói rõ ""Không có dữ liệu về [topic] trong model""
- Khi có số liệu, trình bày dạng bảng hoặc danh sách cho dễ đọc
- Hỗ trợ các câu hỏi về: kích thước, số lượng, vị trí, vật liệu, tham số

PHẠM VI HỖ TRỢ:
- Thống kê cấu kiện (đếm, tổng hợp theo loại/tầng)
- Thông tin kích thước, diện tích, thể tích
- Danh sách phòng, tầng
- Vật liệu, loại cấu kiện (types)
- Dữ liệu ván khuôn (nếu có)
- Shared parameters trên model";

    public AIChatService()
    {
        _httpClient = new HttpClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(60);
        LoadApiKey();
    }

    /// <summary>
    /// Cập nhật context từ model Revit hiện tại.
    /// </summary>
    public void SetModelContext(string context)
    {
        _modelContext = context;
    }

    /// <summary>
    /// Gửi câu hỏi và nhận câu trả lời từ AI.
    /// </summary>
    public async Task<string> AskAsync(string question)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
            return "⚠️ Chưa cấu hình API Key.\n\nVui lòng tạo file:\n%APPDATA%\\CIC-BIM-Addin\\gemini_api_key.txt\n\nNội dung file chỉ chứa API key từ:\nhttps://aistudio.google.com/apikey";

        if (string.IsNullOrWhiteSpace(_modelContext))
            return "⚠️ Chưa có dữ liệu mô hình. Nhấn nút 🔄 Refresh để tải dữ liệu model.";

        // Thêm vào lịch sử
        _history.Add(new ChatMessage("user", question));

        try
        {
            var response = await CallGeminiAsync(question);
            _history.Add(new ChatMessage("model", response));
            return response;
        }
        catch (HttpRequestException ex)
        {
            return $"⚠️ Lỗi kết nối API: {ex.Message}\n\nKiểm tra kết nối mạng và API key.";
        }
        catch (TaskCanceledException)
        {
            return "⚠️ Request timeout (> 60s). Thử lại hoặc đặt câu hỏi ngắn gọn hơn.";
        }
        catch (Exception ex)
        {
            return $"⚠️ Lỗi: {ex.Message}";
        }
    }

    /// <summary>
    /// Xóa lịch sử chat.
    /// </summary>
    public void ClearHistory()
    {
        _history.Clear();
    }

    /// <summary>
    /// Kiểm tra đã có API key chưa.
    /// </summary>
    public bool HasApiKey => !string.IsNullOrWhiteSpace(_apiKey);

    /// <summary>
    /// Cập nhật API key.
    /// </summary>
    public void SetApiKey(string key)
    {
        _apiKey = key?.Trim() ?? "";
        SaveApiKey();
    }

    #region Private — Gemini API Call

    private async Task<string> CallGeminiAsync(string question)
    {
        var url = $"{GeminiBaseUrl}?key={_apiKey}";

        // Build contents array with system instruction, context, and conversation
        var requestBody = BuildRequestBody(question);

        var content = new StringContent(requestBody, Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync(url, content);

        var responseText = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            if (responseText.Contains("API_KEY_INVALID") || responseText.Contains("PERMISSION_DENIED"))
                return "⚠️ API Key không hợp lệ. Kiểm tra lại key tại:\nhttps://aistudio.google.com/apikey";
            if (responseText.Contains("RESOURCE_EXHAUSTED"))
                return "⚠️ Đã hết quota API (free tier: 15 requests/phút).\nĐợi 1 phút rồi thử lại.";
            return $"⚠️ API Error ({(int)response.StatusCode}): {ExtractErrorMessage(responseText)}";
        }

        return ExtractGeminiResponse(responseText);
    }

    /// <summary>
    /// Build JSON request body cho Gemini API.
    /// Sử dụng simple string concat thay vì JSON library để tương thích net48.
    /// </summary>
    private string BuildRequestBody(string question)
    {
        var sb = new StringBuilder();
        sb.Append("{");

        // System instruction
        sb.Append("\"system_instruction\":{\"parts\":[{\"text\":");
        sb.Append(JsonEscape(SystemPrompt));
        sb.Append("}]},");

        // Contents
        sb.Append("\"contents\":[");

        // First message: model context
        sb.Append("{\"role\":\"user\",\"parts\":[{\"text\":");
        sb.Append(JsonEscape("Dưới đây là dữ liệu mô hình BIM Revit hiện tại:\n\n" + _modelContext));
        sb.Append("}]},");

        sb.Append("{\"role\":\"model\",\"parts\":[{\"text\":");
        sb.Append(JsonEscape("Đã nhận dữ liệu mô hình. Tôi sẵn sàng trả lời câu hỏi về model này."));
        sb.Append("}]},");

        // Previous conversation history (keep last 10 exchanges)
        var recentHistory = _history.Count > 20
            ? _history.GetRange(_history.Count - 20, 20)
            : _history;

        foreach (var msg in recentHistory)
        {
            var role = msg.Role == "user" ? "user" : "model";
            sb.Append("{\"role\":\"");
            sb.Append(role);
            sb.Append("\",\"parts\":[{\"text\":");
            sb.Append(JsonEscape(msg.Content));
            sb.Append("}]},");
        }

        // Remove trailing comma
        if (sb[sb.Length - 1] == ',')
            sb.Length--;

        sb.Append("],");

        // Generation config
        sb.Append("\"generationConfig\":{");
        sb.Append("\"temperature\":0.3,");
        sb.Append("\"maxOutputTokens\":2048");
        sb.Append("}");

        sb.Append("}");
        return sb.ToString();
    }

    /// <summary>
    /// Escape string cho JSON (tương thích net48, không cần System.Text.Json).
    /// </summary>
    private static string JsonEscape(string text)
    {
        var sb = new StringBuilder("\"");
        foreach (var c in text)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20)
                        sb.Append($"\\u{(int)c:X4}");
                    else
                        sb.Append(c);
                    break;
            }
        }
        sb.Append("\"");
        return sb.ToString();
    }

    /// <summary>
    /// Trích xuất text response từ Gemini JSON (simple parsing).
    /// </summary>
    private static string ExtractGeminiResponse(string json)
    {
        // Look for "text": "..." in response
        var marker = "\"text\":";
        var idx = json.LastIndexOf(marker);
        if (idx < 0)
        {
            // Try "text" : "..." with spaces
            marker = "\"text\" :";
            idx = json.LastIndexOf(marker);
        }
        if (idx < 0) return "⚠️ Không đọc được phản hồi từ AI.";

        var start = json.IndexOf('"', idx + marker.Length);
        if (start < 0) return "⚠️ Không đọc được phản hồi từ AI.";

        start++; // skip opening quote
        var sb = new StringBuilder();
        for (int i = start; i < json.Length; i++)
        {
            if (json[i] == '\\' && i + 1 < json.Length)
            {
                i++;
                switch (json[i])
                {
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case 'u':
                        if (i + 4 < json.Length)
                        {
                            var hex = json.Substring(i + 1, 4);
                            if (int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var code))
                            {
                                sb.Append((char)code);
                                i += 4;
                            }
                        }
                        break;
                    default: sb.Append(json[i]); break;
                }
            }
            else if (json[i] == '"')
            {
                break; // end of string
            }
            else
            {
                sb.Append(json[i]);
            }
        }

        return sb.ToString();
    }

    private static string ExtractErrorMessage(string json)
    {
        var marker = "\"message\":";
        var idx = json.IndexOf(marker);
        if (idx < 0) return json.Length > 200 ? json.Substring(0, 200) : json;

        var start = json.IndexOf('"', idx + marker.Length);
        if (start < 0) return json.Length > 200 ? json.Substring(0, 200) : json;

        var end = json.IndexOf('"', start + 1);
        if (end < 0) return json.Length > 200 ? json.Substring(0, 200) : json;

        return json.Substring(start + 1, end - start - 1);
    }

    #endregion

    #region API Key Persistence

    private static string GetConfigDir()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "CIC-BIM-Addin");
    }

    private void LoadApiKey()
    {
        try
        {
            var keyFile = Path.Combine(GetConfigDir(), "gemini_api_key.txt");
            if (File.Exists(keyFile))
                _apiKey = File.ReadAllText(keyFile).Trim();
        }
        catch { }
    }

    private void SaveApiKey()
    {
        try
        {
            var dir = GetConfigDir();
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "gemini_api_key.txt"), _apiKey);
        }
        catch { }
    }

    #endregion

    public void Dispose()
    {
        _httpClient?.Dispose();
    }

    private class ChatMessage
    {
        public string Role { get; }
        public string Content { get; }

        public ChatMessage(string role, string content)
        {
            Role = role;
            Content = content;
        }
    }
}
