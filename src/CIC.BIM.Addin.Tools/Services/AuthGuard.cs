namespace CIC.BIM.Addin.Tools.Services;

/// <summary>
/// Helper kiểm tra đăng nhập trước khi chạy command.
/// Nếu chưa login → mở LoginWindow modal.
/// </summary>
public static class AuthGuard
{
    /// <summary>
    /// Kiểm tra user đã đăng nhập chưa.
    /// Nếu chưa → mở LoginWindow.
    /// Trả về true nếu user đã login (hoặc vừa login thành công).
    /// </summary>
    public static bool EnsureLoggedIn()
    {
        if (AuthService.Instance.IsLoggedIn)
            return true;

        // Mở LoginWindow cho user đăng nhập
        var loginWindow = new Views.LoginWindow();
        loginWindow.ShowDialog();

        // Kiểm tra lại sau khi đóng cửa sổ
        return AuthService.Instance.IsLoggedIn;
    }
}
