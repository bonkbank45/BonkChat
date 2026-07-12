using System.Security.Cryptography;
using System.Text;

namespace ChatTwo.Ai;

/// <summary>
/// Encrypts API keys at rest with Windows DPAPI (CurrentUser scope), so the
/// plugin config file no longer contains them in plain text. Sealed values
/// are prefixed and base64-encoded; unsealed values pass through unchanged,
/// which keeps old configs and freshly typed keys working.
/// </summary>
public static class SecretUtil
{
    private const string Prefix = "dpapi:";
    private static readonly byte[] Entropy = "BonkChat"u8.ToArray();

    public static bool IsSealed(string value) => value.StartsWith(Prefix, StringComparison.Ordinal);

    public static string Seal(string value)
    {
        if (string.IsNullOrEmpty(value) || IsSealed(value))
            return value;

        try
        {
            var encrypted = ProtectedData.Protect(Encoding.UTF8.GetBytes(value), Entropy, DataProtectionScope.CurrentUser);
            return Prefix + Convert.ToBase64String(encrypted);
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "Failed to encrypt an API key; keeping it as plain text");
            return value;
        }
    }

    public static string Open(string value)
    {
        if (string.IsNullOrEmpty(value) || !IsSealed(value))
            return value;

        try
        {
            var decrypted = ProtectedData.Unprotect(Convert.FromBase64String(value[Prefix.Length..]), Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(decrypted);
        }
        catch (Exception ex)
        {
            // Wrong Windows user or moved config; the key must be re-entered.
            Plugin.Log.Warning(ex, "Failed to decrypt a stored API key; please re-enter it in the AI settings");
            return string.Empty;
        }
    }
}
