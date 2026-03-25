using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using CIC.BIM.Addin.Tools.Services;

namespace CIC.BIM.Addin.Tools.Views;

public partial class LoginWindow : Window
{
    private static readonly string _rememberFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "CIC-BIM-Addin", "remember_email.txt");

    private bool _passwordVisible = false;
    private bool _isRegisterMode = false;

    public LoginWindow()
    {
        InitializeComponent();
        LoadRememberedEmail();
        UpdateUI();
    }

    // ═══════════════════════════════════════════════
    // UI STATE
    // ═══════════════════════════════════════════════

    private void UpdateUI()
    {
        var auth = AuthService.Instance;

        if (auth.IsLoggedIn)
        {
            PanelLogin.Visibility = Visibility.Collapsed;
            PanelLoggedIn.Visibility = Visibility.Visible;

            var displayName = !string.IsNullOrEmpty(auth.UserFullName)
                ? auth.UserFullName
                : auth.UserEmail;
            TxtAvatar.Text = displayName.Length > 0
                ? displayName[0].ToString().ToUpper()
                : "?";

            TxtUserName.Text = displayName;
            TxtInfoEmail.Text = auth.UserEmail;
            TxtInfoRole.Text = FormatRole(auth.UserRole);
        }
        else
        {
            PanelLogin.Visibility = Visibility.Visible;
            PanelLoggedIn.Visibility = Visibility.Collapsed;
            PanelError.Visibility = Visibility.Collapsed;
        }
    }

    private static string FormatRole(string role) => role switch
    {
        "Admin" => "Quản trị viên",
        "Leadership" => "Ban lãnh đạo",
        "UnitLeader" => "Trưởng đơn vị",
        "AdminUnit" => "Quản trị đơn vị",
        "Accountant" => "Kế toán",
        "ChiefAccountant" => "Kế toán trưởng",
        "Legal" => "Pháp chế",
        "NVKD" => "Nhân viên kinh doanh",
        _ => string.IsNullOrEmpty(role) ? "Người dùng" : role
    };

    // ═══════════════════════════════════════════════
    // PASSWORD SHOW/HIDE
    // ═══════════════════════════════════════════════

    private void BtnTogglePassword_Click(object sender, RoutedEventArgs e)
    {
        _passwordVisible = !_passwordVisible;

        if (_passwordVisible)
        {
            TxtPasswordVisible.Text = TxtPassword.Password;
            TxtPassword.Visibility = Visibility.Collapsed;
            TxtPasswordVisible.Visibility = Visibility.Visible;
            BtnTogglePassword.Content = "🔒";
            TxtPasswordVisible.Focus();
            TxtPasswordVisible.CaretIndex = TxtPasswordVisible.Text.Length;
        }
        else
        {
            TxtPassword.Password = TxtPasswordVisible.Text;
            TxtPasswordVisible.Visibility = Visibility.Collapsed;
            TxtPassword.Visibility = Visibility.Visible;
            BtnTogglePassword.Content = "👁";
            TxtPassword.Focus();
        }
    }

    /// <summary>
    /// Lấy password từ field đang active (PasswordBox hoặc TextBox).
    /// </summary>
    private string GetPassword()
    {
        return _passwordVisible ? TxtPasswordVisible.Text : TxtPassword.Password;
    }

    // ═══════════════════════════════════════════════
    // REMEMBER LOGIN
    // ═══════════════════════════════════════════════

    private void LoadRememberedEmail()
    {
        try
        {
            if (File.Exists(_rememberFile))
            {
                var email = File.ReadAllText(_rememberFile).Trim();
                if (!string.IsNullOrEmpty(email))
                {
                    TxtEmail.Text = email;
                    ChkRemember.IsChecked = true;
                }
            }
        }
        catch { }
    }

    private void SaveRememberedEmail(string email)
    {
        try
        {
            var dir = Path.GetDirectoryName(_rememberFile);
            if (dir != null) Directory.CreateDirectory(dir);

            if (ChkRemember.IsChecked == true && !string.IsNullOrEmpty(email))
                File.WriteAllText(_rememberFile, email);
            else if (File.Exists(_rememberFile))
                File.Delete(_rememberFile);
        }
        catch { }
    }

    // ═══════════════════════════════════════════════
    // EMAIL/PASSWORD LOGIN
    // ═══════════════════════════════════════════════

    // ═══════════════════════════════════════════════
    // REGISTRATION MODE TOGGLE
    // ═══════════════════════════════════════════════

    private void LinkToggleMode_Click(object sender, RoutedEventArgs e)
    {
        _isRegisterMode = !_isRegisterMode;
        
        if (_isRegisterMode)
        {
            TxtTitle.Text = "Tạo tài khoản mới";
            TxtSubtitle.Text = "Điền thông tin bên dưới để bắt đầu";
            PanelFullName.Visibility = Visibility.Visible;
            BtnLogin.Content = "Đăng ký ngay";
            LinkToggleMode.Inlines.Clear();
            LinkToggleMode.Inlines.Add("Đã có tài khoản? Đăng nhập");
            PanelError.Visibility = Visibility.Collapsed;
        }
        else
        {
            TxtTitle.Text = "Bắt đầu với CIC Tools";
            TxtSubtitle.Text = "Chọn phương thức nhanh nhất bên dưới";
            PanelFullName.Visibility = Visibility.Collapsed;
            BtnLogin.Content = "Đăng nhập";
            LinkToggleMode.Inlines.Clear();
            LinkToggleMode.Inlines.Add("Đăng ký tài khoản");
            PanelError.Visibility = Visibility.Collapsed;
        }
    }

    private async void BtnLogin_Click(object sender, RoutedEventArgs e)
    {
        var email = TxtEmail.Text.Trim();
        var password = GetPassword();
        var fullName = TxtFullName.Text.Trim();

        if (string.IsNullOrEmpty(email))
        {
            ShowError("Vui lòng nhập email.");
            TxtEmail.Focus();
            return;
        }

        if (string.IsNullOrEmpty(password))
        {
            ShowError("Vui lòng nhập mật khẩu.");
            TxtPassword.Focus();
            return;
        }

        if (_isRegisterMode && string.IsNullOrEmpty(fullName))
        {
            ShowError("Vui lòng nhập họ và tên.");
            TxtFullName.Focus();
            return;
        }

        SetLoading(true);

        try
        {
            bool success;
            string error;

            if (_isRegisterMode)
            {
                // Gọi API Đăng ký
                (success, error) = await AuthService.Instance.SignUpAsync(email, password, fullName);
                if (success)
                {
                    MessageBox.Show(
                        "Đăng ký thành công! Vui lòng kiểm tra email để xác nhận tài khoản (nếu cần) và đăng nhập.",
                        "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
                    
                    // Chuyển về chế độ đăng nhập
                    LinkToggleMode_Click(null, null);
                }
            }
            else
            {
                // Gọi API Đăng nhập
                (success, error) = await AuthService.Instance.LoginAsync(email, password);
                if (success)
                {
                    SaveRememberedEmail(email);
                    UpdateUI();
                }
            }

            if (!success)
            {
                ShowError(error);
            }
        }
        catch (Exception ex)
        {
            ShowError($"Lỗi: {ex.Message}");
        }
        finally
        {
            SetLoading(false);
        }
    }

    private void SetLoading(bool isLoading)
    {
        BtnLogin.IsEnabled = !isLoading;
        BtnGoogleSignIn.IsEnabled = !isLoading;
        PanelLoading.Visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;
        PanelError.Visibility = Visibility.Collapsed;
    }

    // ═══════════════════════════════════════════════
    // GOOGLE OAUTH
    // ═══════════════════════════════════════════════

    private async void BtnGoogleSignIn_Click(object sender, RoutedEventArgs e)
    {
        BtnLogin.IsEnabled = false;
        BtnGoogleSignIn.IsEnabled = false;
        PanelLoading.Visibility = Visibility.Visible;
        PanelError.Visibility = Visibility.Collapsed;

        try
        {
            var (success, error) = await AuthService.Instance.SignInWithGoogleAsync();

            if (success)
            {
                SaveRememberedEmail(AuthService.Instance.UserEmail);
                UpdateUI();
                
                // If it was a first-time login (registration), show a welcome message
                if (AuthService.Instance.IsLoggedIn)
                {
                    // Basic check: if profile was just created
                    // (In a real app, we might check a 'IsNewUser' flag from Supabase)
                }
            }
            else
            {
                ShowError(error);
            }
        }
        catch (Exception ex)
        {
            ShowError($"Lỗi: {ex.Message}");
        }
        finally
        {
            BtnLogin.IsEnabled = true;
            BtnGoogleSignIn.IsEnabled = true;
            PanelLoading.Visibility = Visibility.Collapsed;
        }
    }

    // ═══════════════════════════════════════════════
    // LOGOUT
    // ═══════════════════════════════════════════════

    private void BtnLogout_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "Bạn có chắc muốn đăng xuất?",
            "CIC Tools",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            AuthService.Instance.Logout();
            UpdateUI();
            TxtPassword.Password = "";
            TxtPasswordVisible.Text = "";
        }
    }

    // ═══════════════════════════════════════════════
    // COMMON
    // ═══════════════════════════════════════════════

    private void ShowError(string message)
    {
        TxtError.Text = message;
        PanelError.Visibility = Visibility.Visible;
    }

    private void Input_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            BtnLogin_Click(sender, e);
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
