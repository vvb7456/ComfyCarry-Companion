using System.Security.Cryptography;
using System.Text;

namespace ComfyCarry.Services;

/// <summary>
/// DPAPI (CurrentUser) 加密存储字符串。不落明文。
/// 注：DPAPI 仅 Windows 可用，正好本应用目标 Windows。
/// </summary>
public sealed class SecretStore
{
    public string Protect(string plain)
    {
        if (string.IsNullOrEmpty(plain)) return string.Empty;
        var bytes = Encoding.UTF8.GetBytes(plain);
        var cipher = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(cipher);
    }

    public string Unprotect(string? cipherBase64)
    {
        if (string.IsNullOrEmpty(cipherBase64)) return string.Empty;
        try
        {
            var cipher = Convert.FromBase64String(cipherBase64);
            var bytes = ProtectedData.Unprotect(cipher, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch
        {
            return string.Empty;
        }
    }
}
