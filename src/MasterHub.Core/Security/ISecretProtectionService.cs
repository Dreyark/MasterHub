namespace MasterHub.Core.Security;

/// <summary>
/// Usługa bezpiecznego szyfrowania i odszyfrowywania poufnych danych uwierzytelniających
/// (haseł OBS, tokenów Discord, kluczy API YouTube/Twitch/Streamlabs)
/// z użyciem Windows DPAPI (Data Protection API) i unikalnej entropii aplikacji.
/// </summary>
public interface ISecretProtectionService
{
    /// <summary>
    /// Szyfruje podany tekst jawny. Jeśli tekst jest już zaszyfrowany (zawiera prefiks enc:v1:),
    /// zwraca go bez powtórnego szyfrowania.
    /// </summary>
    string? Protect(string? plainText);

    /// <summary>
    /// Odszyfrowuje zaszyfrowany ciąg znaków. Jeśli ciąg nie jest zaszyfrowany
    /// (brak prefiksu enc:v1: - np. dane ze starszych wersji aplikacji), zwraca go jako tekst jawny.
    /// </summary>
    string? Unprotect(string? cipherText);

    /// <summary>
    /// Sprawdza, czy podana wartość jest już oznaczona jako zaszyfrowana.
    /// </summary>
    bool IsProtected(string? value);
}
