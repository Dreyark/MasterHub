using System;
using System.Security.Cryptography;
using System.Text;

namespace MasterHub.Core.Security;

/// <summary>
/// Implementacja ochrony sekretów w MasterHub oparta na Windows DPAPI (Data Protection API)
/// z unikalną dla aplikacji entropią i wersjonowanym nagłówkiem "enc:v1:".
/// 
/// Dlaczego DPAPI jest lepsze od zwykłego statycznego AES-256 w aplikacjach desktopowych:
/// - Zwykły klucz AES-256 w kodzie .NET można łatwo wyodrębnić dekompilatorem (ILSpy / dnSpy).
/// - DPAPI wykorzystuje sprzętowe i systemowe klucze konta użytkownika systemu Windows zarządzane przez LSASS.
/// - Skradziony plik JSON z innego komputera lub konta użytkownika jest bezużyteczny dla atakującego.
/// </summary>
public class SecretProtectionService : ISecretProtectionService
{
    public const string ProtectionPrefix = "enc:v1:";

    // Entropia aplikacyjna izolująca sekrety MasterHub przed odczytem przez inne programy używające DPAPI
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("MasterHub.Core.Security.Entropy.v1@2026");

    public bool IsProtected(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        return value.StartsWith(ProtectionPrefix, StringComparison.Ordinal);
    }

    public string? Protect(string? plainText)
    {
        if (string.IsNullOrEmpty(plainText))
            return plainText;

        if (IsProtected(plainText))
            return plainText;

        try
        {
            byte[] plainBytes = Encoding.UTF8.GetBytes(plainText);
            byte[] cipherBytes = ProtectedData.Protect(plainBytes, Entropy, DataProtectionScope.CurrentUser);
            return ProtectionPrefix + Convert.ToBase64String(cipherBytes);
        }
        catch (Exception)
        {
            // W razie braku wsparcia DPAPI w specyficznym środowisku zachowujemy wartość
            return plainText;
        }
    }

    public string? Unprotect(string? cipherText)
    {
        if (string.IsNullOrEmpty(cipherText))
            return cipherText;

        // Wsteczna kompatybilność: dane zapisane przed wprowadzeniem szyfrowania (tekst jawny)
        if (!IsProtected(cipherText))
            return cipherText;

        try
        {
            string payload = cipherText[ProtectionPrefix.Length..];
            byte[] cipherBytes = Convert.FromBase64String(payload);
            byte[] plainBytes = ProtectedData.Unprotect(cipherBytes, Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plainBytes);
        }
        catch (CryptographicException)
        {
            // Błąd odszyfrowania (np. plik przeniesiony z innej maszyny lub innego konta Windows)
            return string.Empty;
        }
        catch (FormatException)
        {
            // Błędny format Base64
            return string.Empty;
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }
}
