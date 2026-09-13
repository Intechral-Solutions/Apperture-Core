using System.Security.Cryptography;
using System.Text;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// High-entropy one-time recovery codes. Plaintext exists only at generation time.
/// </summary>
public static class MfaRecoveryCodes
{
    public const int DefaultCount = 10;
    public const int GroupSize = 4;
    public const int GroupCount = 3;
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public static IReadOnlyList<string> Generate(int count = DefaultCount)
    {
        if (count < 1)
            throw new ArgumentOutOfRangeException(nameof(count));

        var codes = new string[count];
        var bytes = new byte[GroupSize * GroupCount];
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < count; i++)
        {
            string formatted;
            do
            {
                RandomNumberGenerator.Fill(bytes);
                var chars = new char[GroupSize * GroupCount];
                for (var j = 0; j < chars.Length; j++)
                    chars[j] = Alphabet[bytes[j] % Alphabet.Length];
                formatted = string.Join('-', Enumerable.Range(0, GroupCount)
                    .Select(g => new string(chars, g * GroupSize, GroupSize)));
            } while (!seen.Add(formatted));
            codes[i] = formatted;
        }

        return codes;
    }

    public static string Normalize(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return "";
        var chars = code.Where(c => c != '-' && c != ' ' && c != '\t').Select(char.ToUpperInvariant).ToArray();
        return new string(chars);
    }

    public static string Fingerprint(string normalizedCode)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalizedCode));
        return Convert.ToHexString(hash);
    }
}
