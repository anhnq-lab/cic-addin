using System.IO;
using System.Text.Json;

namespace CIC.BIM.Addin.Analytics.Models;

/// <summary>
/// User profile configuration, loaded from %APPDATA%/CIC/BIM/user_profile.json
/// Used to identify modeler department/role for team analytics.
/// </summary>
public class UserProfile
{
    public string UserName { get; set; } = Environment.UserName;
    public string DisplayName { get; set; } = Environment.UserName;
    public string Department { get; set; } = "General";  // MEP, Architecture, Structure
    public string Role { get; set; } = "Modeler";        // Modeler, Lead, Coordinator

    private static readonly string ProfilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "CIC", "BIM", "user_profile.json");

    /// <summary>
    /// Load user profile from disk. Creates default if not exists.
    /// </summary>
    public static UserProfile Load()
    {
        try
        {
            if (File.Exists(ProfilePath))
            {
                var json = File.ReadAllText(ProfilePath);
                var profile = JsonSerializer.Deserialize<UserProfile>(json);
                if (profile != null)
                {
                    profile.UserName = Environment.UserName; // Always override with actual
                    return profile;
                }
            }
        }
        catch { /* Use default */ }

        // Create default profile
        var defaultProfile = new UserProfile();
        Save(defaultProfile);
        return defaultProfile;
    }

    /// <summary>
    /// Save profile to disk
    /// </summary>
    public static void Save(UserProfile profile)
    {
        try
        {
            var dir = Path.GetDirectoryName(ProfilePath)!;
            Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(profile, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            File.WriteAllText(ProfilePath, json);
        }
        catch { /* Silent */ }
    }
}
