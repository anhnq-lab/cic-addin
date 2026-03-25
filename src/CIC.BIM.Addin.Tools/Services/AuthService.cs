using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace CIC.BIM.Addin.Tools.Services;

/// <summary>
/// Singleton service quản lý xác thực qua Supabase Auth REST API.
/// Lưu session persistent tại %APPDATA%/CIC-BIM-Addin/auth_session.json.
/// Tương thích .NET Framework 4.8 (không dùng System.Text.Json).
/// </summary>
public class AuthService : IDisposable
{
    // ═══ Supabase Config ═══
    private const string SupabaseUrl = "https://jyohocjsnsyfgfsmjfqx.supabase.co";
    private const string SupabaseAnonKey = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6Imp5b2hvY2pzbnN5Zmdmc21qZnF4Iiwicm9sZSI6ImFub24iLCJpYXQiOjE3Njk0MTExODMsImV4cCI6MjA4NDk4NzE4M30.zV5sf6Pso4LX4kRV6bBIEahCu6qIP1GJO505AbYR1n0";

    // ═══ Singleton ═══
    private static AuthService? _instance;
    private static readonly object _lock = new();

    public static AuthService Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    _instance ??= new AuthService();
                }
            }
            return _instance;
        }
    }

    // ═══ State ═══
    private readonly HttpClient _httpClient;
    private string _accessToken = "";
    private string _refreshToken = "";
    private DateTime _expiresAt = DateTime.MinValue;

    // User info
    public string UserId { get; private set; } = "";
    public string UserEmail { get; private set; } = "";
    public string UserFullName { get; private set; } = "";
    public string UserRole { get; private set; } = "";
    public string UserAvatarUrl { get; private set; } = "";

    public bool IsLoggedIn => !string.IsNullOrEmpty(_accessToken) && !string.IsNullOrEmpty(UserId);
    public string AccessToken => _accessToken;

    // ═══ Events ═══
    public event Action? OnLoginChanged;

    private AuthService()
    {
        _httpClient = new HttpClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
        _httpClient.DefaultRequestHeaders.Add("apikey", SupabaseAnonKey);
        LoadSession();
    }

    /// <summary>
    /// Đăng nhập bằng email/password.
    /// Trả về (success, errorMessage).
    /// </summary>
    public async Task<(bool Success, string Error)> LoginAsync(string email, string password)
    {
        try
        {
            var url = $"{SupabaseUrl}/auth/v1/token?grant_type=password";

            var body = $"{{\"email\":{JsonEscape(email)},\"password\":{JsonEscape(password)}}}";
            var content = new StringContent(body, Encoding.UTF8, "application/json");

            // Remove old auth header if present
            _httpClient.DefaultRequestHeaders.Remove("Authorization");

            var response = await _httpClient.PostAsync(url, content);
            var responseText = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                var errorMsg = ExtractJsonValue(responseText, "error_description")
                    ?? ExtractJsonValue(responseText, "msg")
                    ?? ExtractJsonValue(responseText, "message")
                    ?? "Đăng nhập thất bại";

                // Translate common errors
                if (errorMsg.Contains("Invalid login credentials"))
                    errorMsg = "Email hoặc mật khẩu không đúng";
                else if (errorMsg.Contains("Email not confirmed"))
                    errorMsg = "Email chưa được xác thực";

                return (false, errorMsg);
            }

            // Parse successful response
            _accessToken = ExtractJsonValue(responseText, "access_token") ?? "";
            _refreshToken = ExtractJsonValue(responseText, "refresh_token") ?? "";

            var expiresIn = ExtractJsonValue(responseText, "expires_in") ?? "3600";
            if (int.TryParse(expiresIn, out var seconds))
                _expiresAt = DateTime.UtcNow.AddSeconds(seconds);

            // Extract user info from nested "user" object
            ParseUserFromResponse(responseText);

            // Fetch profile for full_name and role
            await FetchProfileAsync();

            SaveSession();
            OnLoginChanged?.Invoke();

            return (true, "");
        }
        catch (HttpRequestException ex)
        {
            return (false, $"Lỗi kết nối: {ex.Message}\nKiểm tra kết nối mạng.");
        }
        catch (TaskCanceledException)
        {
            return (false, "Kết nối timeout. Thử lại sau.");
        }
        catch (Exception ex)
        {
            return (false, $"Lỗi: {ex.Message}");
        }
    }

    /// <summary>
    /// Đăng ký tài khoản mới bằng email/password.
    /// Trả về (success, errorMessage).
    /// </summary>
    public async Task<(bool Success, string Error)> SignUpAsync(string email, string password, string fullName = "")
    {
        try
        {
            var url = $"{SupabaseUrl}/auth/v1/signup";

            // Build JSON body with optional full_name in user_metadata
            var bodyBuilder = new StringBuilder();
            bodyBuilder.Append("{");
            bodyBuilder.Append($"\"email\":{JsonEscape(email)},");
            bodyBuilder.Append($"\"password\":{JsonEscape(password)}");
            if (!string.IsNullOrWhiteSpace(fullName))
            {
                bodyBuilder.Append($",\"data\":{{\"full_name\":{JsonEscape(fullName)}}}");
            }
            bodyBuilder.Append("}");

            var content = new StringContent(bodyBuilder.ToString(), Encoding.UTF8, "application/json");

            _httpClient.DefaultRequestHeaders.Remove("Authorization");

            var response = await _httpClient.PostAsync(url, content);
            var responseText = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                var errorMsg = ExtractJsonValue(responseText, "error_description")
                    ?? ExtractJsonValue(responseText, "msg")
                    ?? ExtractJsonValue(responseText, "message")
                    ?? "Đăng ký thất bại";

                // Translate common errors
                if (errorMsg.Contains("already registered") || errorMsg.Contains("already been registered"))
                    errorMsg = "Email này đã được đăng ký. Vui lòng đăng nhập.";
                else if (errorMsg.Contains("Password should be"))
                    errorMsg = "Mật khẩu phải có ít nhất 6 ký tự.";
                else if (errorMsg.Contains("valid email"))
                    errorMsg = "Email không hợp lệ.";

                return (false, errorMsg);
            }

            // Check if email confirmation is required
            var accessToken = ExtractJsonValue(responseText, "access_token");
            if (string.IsNullOrEmpty(accessToken))
            {
                // Supabase requires email confirmation
                return (true, "CONFIRM_EMAIL");
            }

            // Auto-login after signup (no email confirmation required)
            _accessToken = accessToken ?? "";
            _refreshToken = ExtractJsonValue(responseText, "refresh_token") ?? "";

            var expiresIn = ExtractJsonValue(responseText, "expires_in") ?? "3600";
            if (int.TryParse(expiresIn, out var seconds))
                _expiresAt = DateTime.UtcNow.AddSeconds(seconds);

            ParseUserFromResponse(responseText);
            await FetchProfileAsync();
            SaveSession();
            OnLoginChanged?.Invoke();

            return (true, "");
        }
        catch (HttpRequestException ex)
        {
            return (false, $"Lỗi kết nối: {ex.Message}\nKiểm tra kết nối mạng.");
        }
        catch (TaskCanceledException)
        {
            return (false, "Kết nối timeout. Thử lại sau.");
        }
        catch (Exception ex)
        {
            return (false, $"Lỗi: {ex.Message}");
        }
    }

    /// <summary>
    /// Đăng nhập bằng Google OAuth.
    /// Mở trình duyệt → Google login → callback về local server → lấy tokens.
    /// </summary>
    public async Task<(bool Success, string Error)> SignInWithGoogleAsync()
    {
        System.Net.HttpListener? listener = null;
        try
        {
            // Find available port
            var port = FindAvailablePort();
            var redirectUrl = $"http://localhost:{port}/callback";

            // Start local HTTP listener
            listener = new System.Net.HttpListener();
            listener.Prefixes.Add($"http://localhost:{port}/");
            listener.Start();

            // Open browser to Supabase OAuth
            var authUrl = $"{SupabaseUrl}/auth/v1/authorize?provider=google&redirect_to={Uri.EscapeDataString(redirectUrl)}";
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = authUrl,
                UseShellExecute = true
            });

            // Wait for callback (timeout 5 minutes)
            var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromMinutes(5));

            // Step 1: Supabase redirects with tokens in URL fragment (#access_token=...)
            // Serve HTML page that reads fragment and posts back
            var context = await listener.GetContextAsync();
            var callbackHtml = @"<!DOCTYPE html>
<html><head><meta charset='utf-8'><title>CIC Tools</title>
<style>
body{font-family:system-ui;background:#1E1E2E;color:#CDD6F4;display:flex;align-items:center;justify-content:center;height:100vh;margin:0}
.card{background:#313244;padding:40px;border-radius:16px;text-align:center;max-width:400px}
h2{color:#89B4FA;margin-bottom:8px}p{color:#A6ADC8}
</style></head><body>
<div class='card'><h2>✅ Đăng nhập thành công!</h2><p>Bạn có thể đóng tab này và quay lại Revit.</p></div>
<script>
if(window.location.hash){
  var params=new URLSearchParams(window.location.hash.substring(1));
  var data={access_token:params.get('access_token'),refresh_token:params.get('refresh_token'),expires_in:params.get('expires_in')};
  fetch('/token',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(data)});
}else if(window.location.search){
  var params=new URLSearchParams(window.location.search);
  var code=params.get('code');
  if(code) fetch('/token',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({code:code})});
}
</script></body></html>";

            var buffer = Encoding.UTF8.GetBytes(callbackHtml);
            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.ContentLength64 = buffer.Length;
            await context.Response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
            context.Response.Close();

            // Step 2: Wait for the POST /token from the HTML page
            var tokenContext = await listener.GetContextAsync();

            if (tokenContext.Request.HttpMethod == "POST" && tokenContext.Request.Url?.AbsolutePath == "/token")
            {
                using var reader = new StreamReader(tokenContext.Request.InputStream, Encoding.UTF8);
                var json = await reader.ReadToEndAsync();

                // Send OK response
                var okBytes = Encoding.UTF8.GetBytes("OK");
                tokenContext.Response.StatusCode = 200;
                tokenContext.Response.ContentLength64 = okBytes.Length;
                await tokenContext.Response.OutputStream.WriteAsync(okBytes, 0, okBytes.Length);
                tokenContext.Response.Close();

                // Check if we got an auth code (PKCE flow)
                var code = ExtractJsonValue(json, "code");
                if (!string.IsNullOrEmpty(code))
                {
                    // Exchange code for tokens
                    return await ExchangeCodeForTokenAsync(code, redirectUrl);
                }

                // Direct token flow
                var accessToken = ExtractJsonValue(json, "access_token");
                var refreshToken = ExtractJsonValue(json, "refresh_token");

                if (string.IsNullOrEmpty(accessToken))
                    return (false, "Không nhận được token từ Google. Thử lại.");

                _accessToken = accessToken;
                _refreshToken = refreshToken ?? "";

                var expiresIn = ExtractJsonValue(json, "expires_in") ?? "3600";
                if (int.TryParse(expiresIn, out var seconds))
                    _expiresAt = DateTime.UtcNow.AddSeconds(seconds);

                // Fetch user info
                await FetchUserInfoAsync();
                await FetchProfileAsync();
                SaveSession();
                OnLoginChanged?.Invoke();

                return (true, "");
            }

            return (false, "Không nhận được phản hồi từ trình duyệt.");
        }
        catch (TaskCanceledException)
        {
            return (false, "Hết thời gian chờ đăng nhập Google (5 phút).");
        }
        catch (Exception ex)
        {
            return (false, $"Lỗi: {ex.Message}");
        }
        finally
        {
            try { listener?.Stop(); listener?.Close(); } catch { }
        }
    }

    /// <summary>
    /// Exchange auth code for tokens (PKCE flow).
    /// </summary>
    private async Task<(bool Success, string Error)> ExchangeCodeForTokenAsync(string code, string redirectUrl)
    {
        try
        {
            var url = $"{SupabaseUrl}/auth/v1/token?grant_type=authorization_code";
            var body = $"{{\"auth_code\":{JsonEscape(code)},\"code_verifier\":\"\",\"redirect_to\":{JsonEscape(redirectUrl)}}}";
            var content = new StringContent(body, Encoding.UTF8, "application/json");

            _httpClient.DefaultRequestHeaders.Remove("Authorization");
            var response = await _httpClient.PostAsync(url, content);
            var responseText = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                var errorMsg = ExtractJsonValue(responseText, "error_description")
                    ?? ExtractJsonValue(responseText, "msg")
                    ?? "Không thể đổi mã xác thực.";
                return (false, errorMsg);
            }

            _accessToken = ExtractJsonValue(responseText, "access_token") ?? "";
            _refreshToken = ExtractJsonValue(responseText, "refresh_token") ?? "";

            var expiresIn = ExtractJsonValue(responseText, "expires_in") ?? "3600";
            if (int.TryParse(expiresIn, out var seconds))
                _expiresAt = DateTime.UtcNow.AddSeconds(seconds);

            ParseUserFromResponse(responseText);
            await FetchProfileAsync();
            SaveSession();
            OnLoginChanged?.Invoke();

            return (true, "");
        }
        catch (Exception ex)
        {
            return (false, $"Lỗi đổi mã: {ex.Message}");
        }
    }

    /// <summary>
    /// Lấy thông tin user từ access token.
    /// </summary>
    private async Task FetchUserInfoAsync()
    {
        try
        {
            _httpClient.DefaultRequestHeaders.Remove("Authorization");
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_accessToken}");

            var response = await _httpClient.GetAsync($"{SupabaseUrl}/auth/v1/user");
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                ParseUserFromResponse(json);
            }
        }
        catch { }
    }

    /// <summary>
    /// Tìm port khả dụng.
    /// </summary>
    private static int FindAvailablePort()
    {
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    /// <summary>
    /// Làm mới access token bằng refresh token.
    /// </summary>
    public async Task<bool> RefreshTokenAsync()
    {
        if (string.IsNullOrEmpty(_refreshToken))
            return false;

        try
        {
            var url = $"{SupabaseUrl}/auth/v1/token?grant_type=refresh_token";
            var body = $"{{\"refresh_token\":{JsonEscape(_refreshToken)}}}";
            var content = new StringContent(body, Encoding.UTF8, "application/json");

            _httpClient.DefaultRequestHeaders.Remove("Authorization");

            var response = await _httpClient.PostAsync(url, content);
            var responseText = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                // Refresh failed → user needs to login again
                ClearSession();
                return false;
            }

            _accessToken = ExtractJsonValue(responseText, "access_token") ?? "";
            _refreshToken = ExtractJsonValue(responseText, "refresh_token") ?? "";

            var expiresIn = ExtractJsonValue(responseText, "expires_in") ?? "3600";
            if (int.TryParse(expiresIn, out var seconds))
                _expiresAt = DateTime.UtcNow.AddSeconds(seconds);

            ParseUserFromResponse(responseText);
            SaveSession();

            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Đảm bảo access token còn hiệu lực. Tự refresh nếu hết hạn.
    /// </summary>
    public async Task<bool> EnsureValidTokenAsync()
    {
        if (!IsLoggedIn)
            return false;

        // Refresh if token expires within 5 minutes
        if (DateTime.UtcNow.AddMinutes(5) >= _expiresAt)
        {
            return await RefreshTokenAsync();
        }

        return true;
    }

    /// <summary>
    /// Đăng xuất — xóa session local.
    /// </summary>
    public void Logout()
    {
        // Fire-and-forget server logout
        try
        {
            if (!string.IsNullOrEmpty(_accessToken))
            {
                var request = new HttpRequestMessage(HttpMethod.Post, $"{SupabaseUrl}/auth/v1/logout");
                request.Headers.Add("Authorization", $"Bearer {_accessToken}");
                _ = _httpClient.SendAsync(request);
            }
        }
        catch { }

        ClearSession();
        OnLoginChanged?.Invoke();
    }

    /// <summary>
    /// Lấy thông tin profile (full_name, role) từ bảng profiles.
    /// </summary>
    private async Task FetchProfileAsync()
    {
        if (string.IsNullOrEmpty(_accessToken) || string.IsNullOrEmpty(UserId))
            return;

        try
        {
            var url = $"{SupabaseUrl}/rest/v1/profiles?id=eq.{UserId}&select=full_name,role,avatar_url,email";
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("Authorization", $"Bearer {_accessToken}");

            var response = await _httpClient.SendAsync(request);
            var responseText = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode && responseText.Length > 5)
            {
                var fullName = ExtractJsonValue(responseText, "full_name");
                if (!string.IsNullOrEmpty(fullName))
                    UserFullName = fullName;

                var role = ExtractJsonValue(responseText, "role");
                if (!string.IsNullOrEmpty(role))
                    UserRole = role;

                var avatar = ExtractJsonValue(responseText, "avatar_url");
                if (!string.IsNullOrEmpty(avatar))
                    UserAvatarUrl = avatar;
            }
        }
        catch { }
    }

    #region JSON Helpers (net48-compatible)

    private void ParseUserFromResponse(string json)
    {
        // Find "user" object and extract id, email
        var userIdx = json.IndexOf("\"user\"");
        if (userIdx < 0) return;

        var userBlock = json.Substring(userIdx);

        UserId = ExtractJsonValue(userBlock, "id") ?? "";
        UserEmail = ExtractJsonValue(userBlock, "email") ?? "";

        // Try to get full_name from user_metadata
        var metaName = ExtractNestedJsonValue(userBlock, "user_metadata", "full_name");
        if (!string.IsNullOrEmpty(metaName))
            UserFullName = metaName;
    }

    /// <summary>
    /// Trích xuất giá trị string đơn giản từ JSON (tương thích net48).
    /// </summary>
    private static string? ExtractJsonValue(string json, string key)
    {
        var pattern = $"\"{key}\"";
        var idx = json.IndexOf(pattern);
        if (idx < 0) return null;

        var colonIdx = json.IndexOf(':', idx + pattern.Length);
        if (colonIdx < 0) return null;

        // Skip whitespace after colon
        var valueStart = colonIdx + 1;
        while (valueStart < json.Length && (json[valueStart] == ' ' || json[valueStart] == '\t'))
            valueStart++;

        if (valueStart >= json.Length) return null;

        // Check if value is a string (starts with ")
        if (json[valueStart] == '"')
        {
            var strStart = valueStart + 1;
            var sb = new StringBuilder();
            for (int i = strStart; i < json.Length; i++)
            {
                if (json[i] == '\\' && i + 1 < json.Length)
                {
                    i++;
                    switch (json[i])
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        default: sb.Append(json[i]); break;
                    }
                }
                else if (json[i] == '"')
                {
                    break;
                }
                else
                {
                    sb.Append(json[i]);
                }
            }
            return sb.ToString();
        }

        // Check if value is null
        if (json.Length > valueStart + 3 && json.Substring(valueStart, 4) == "null")
            return null;

        // Number or boolean — read until , or } or ]
        var numEnd = valueStart;
        while (numEnd < json.Length && json[numEnd] != ',' && json[numEnd] != '}' && json[numEnd] != ']')
            numEnd++;

        return json.Substring(valueStart, numEnd - valueStart).Trim();
    }

    /// <summary>
    /// Trích xuất giá trị từ object lồng: "parentKey": { "childKey": "value" }
    /// </summary>
    private static string? ExtractNestedJsonValue(string json, string parentKey, string childKey)
    {
        var parentPattern = $"\"{parentKey}\"";
        var parentIdx = json.IndexOf(parentPattern);
        if (parentIdx < 0) return null;

        var braceIdx = json.IndexOf('{', parentIdx + parentPattern.Length);
        if (braceIdx < 0) return null;

        // Find matching closing brace
        var depth = 1;
        var endIdx = braceIdx + 1;
        while (endIdx < json.Length && depth > 0)
        {
            if (json[endIdx] == '{') depth++;
            else if (json[endIdx] == '}') depth--;
            endIdx++;
        }

        var innerJson = json.Substring(braceIdx, endIdx - braceIdx);
        return ExtractJsonValue(innerJson, childKey);
    }

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

    #endregion

    #region Session Persistence

    private static string GetSessionPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "CIC-BIM-Addin", "auth_session.json");
    }

    private void SaveSession()
    {
        try
        {
            var dir = Path.GetDirectoryName(GetSessionPath())!;
            Directory.CreateDirectory(dir);

            var json = new StringBuilder();
            json.Append("{");
            json.Append($"\"access_token\":{JsonEscape(_accessToken)},");
            json.Append($"\"refresh_token\":{JsonEscape(_refreshToken)},");
            json.Append($"\"expires_at\":{JsonEscape(_expiresAt.ToString("o"))},");
            json.Append($"\"user_id\":{JsonEscape(UserId)},");
            json.Append($"\"user_email\":{JsonEscape(UserEmail)},");
            json.Append($"\"user_full_name\":{JsonEscape(UserFullName)},");
            json.Append($"\"user_role\":{JsonEscape(UserRole)},");
            json.Append($"\"user_avatar_url\":{JsonEscape(UserAvatarUrl)}");
            json.Append("}");

            File.WriteAllText(GetSessionPath(), json.ToString(), Encoding.UTF8);
        }
        catch { }
    }

    private void LoadSession()
    {
        try
        {
            var path = GetSessionPath();
            if (!File.Exists(path)) return;

            var json = File.ReadAllText(path, Encoding.UTF8);

            _accessToken = ExtractJsonValue(json, "access_token") ?? "";
            _refreshToken = ExtractJsonValue(json, "refresh_token") ?? "";
            UserId = ExtractJsonValue(json, "user_id") ?? "";
            UserEmail = ExtractJsonValue(json, "user_email") ?? "";
            UserFullName = ExtractJsonValue(json, "user_full_name") ?? "";
            UserRole = ExtractJsonValue(json, "user_role") ?? "";
            UserAvatarUrl = ExtractJsonValue(json, "user_avatar_url") ?? "";

            var expiresAtStr = ExtractJsonValue(json, "expires_at");
            if (!string.IsNullOrEmpty(expiresAtStr) && DateTime.TryParse(expiresAtStr, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
                _expiresAt = dt;
        }
        catch { }
    }

    private void ClearSession()
    {
        _accessToken = "";
        _refreshToken = "";
        _expiresAt = DateTime.MinValue;
        UserId = "";
        UserEmail = "";
        UserFullName = "";
        UserRole = "";
        UserAvatarUrl = "";

        try
        {
            var path = GetSessionPath();
            if (File.Exists(path))
                File.Delete(path);
        }
        catch { }
    }

    #endregion

    public void Dispose()
    {
        _httpClient?.Dispose();
    }
}
