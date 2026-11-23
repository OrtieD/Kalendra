using System.Security.Cryptography;
using System.Text;

namespace Kalendra.Services;

/// <summary>
/// Protects the app-specific password with Windows DPAPI, bound to the current Windows user.
/// On another PC (e.g. portable version on a USB stick) it cannot be decrypted and the app asks again.
/// </summary>
public static class SecretStore
{
    public static string Protect(string plain)
    {
        var bytes = Encoding.UTF8.GetBytes(plain);
        if (OperatingSystem.IsWindows())
            return "dpapi:" + Convert.ToBase64String(ProtectedData.Protect(bytes, Entropy, DataProtectionScope.CurrentUser));
        return "plain:" + Convert.ToBase64String(bytes); // only used for development on Linux
    }

    public static string? Unprotect(string stored)
    {
        try
        {
            if (stored.StartsWith("dpapi:") && OperatingSystem.IsWindows())
                return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(stored[6..]), Entropy, DataProtectionScope.CurrentUser));
            if (stored.StartsWith("plain:"))
                return Encoding.UTF8.GetString(Convert.FromBase64String(stored[6..]));
        }
        catch { }
        return null;
    }

    static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Kalendra-iCloud");
}
