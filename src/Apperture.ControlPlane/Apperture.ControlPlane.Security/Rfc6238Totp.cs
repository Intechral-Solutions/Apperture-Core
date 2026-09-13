using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// RFC 6238 TOTP (HMAC-SHA1, 6 digits, 30-second period) for native authenticator-app MFA.
/// </summary>
public static class Rfc6238Totp
{
    public const int DefaultDigits = 6;
    public const int DefaultPeriodSeconds = 30;
    public const int DefaultWindowSteps = 1;
    public const int SecretSizeBytes = 20;

    public static byte[] GenerateSecret()
    {
        var secret = new byte[SecretSizeBytes];
        RandomNumberGenerator.Fill(secret);
        return secret;
    }

    public static string FormatOtpAuthUri(string accountName, byte[] secret, string issuer = "Apperture")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountName);
        ArgumentNullException.ThrowIfNull(secret);
        var label = Uri.EscapeDataString($"{issuer}:{accountName.Trim()}");
        var encodedSecret = Base32.Encode(secret);
        var encodedIssuer = Uri.EscapeDataString(issuer);
        return $"otpauth://totp/{label}?secret={encodedSecret}&issuer={encodedIssuer}&algorithm=SHA1&digits={DefaultDigits}&period={DefaultPeriodSeconds}";
    }

    public static string Compute(byte[] secret, long unixTimeSeconds, int periodSeconds = DefaultPeriodSeconds, int digits = DefaultDigits)
    {
        var step = unixTimeSeconds / periodSeconds;
        return ComputeForStep(secret, step, digits);
    }

    public static bool Verify(
        byte[] secret,
        string code,
        long unixTimeSeconds,
        long? lastUsedStep,
        out long matchedStep,
        int windowSteps = DefaultWindowSteps,
        int periodSeconds = DefaultPeriodSeconds,
        int digits = DefaultDigits)
    {
        matchedStep = 0;
        if (string.IsNullOrWhiteSpace(code) || secret.Length == 0)
            return false;

        var normalized = new string(code.Where(char.IsDigit).ToArray());
        if (normalized.Length != digits)
            return false;

        var currentStep = unixTimeSeconds / periodSeconds;
        for (var delta = -windowSteps; delta <= windowSteps; delta++)
        {
            var step = currentStep + delta;
            if (step < 0)
                continue;
            if (lastUsedStep is { } used && step <= used)
                continue;
            if (CryptographicOperations.FixedTimeEquals(
                    Encoding.ASCII.GetBytes(ComputeForStep(secret, step, digits)),
                    Encoding.ASCII.GetBytes(normalized)))
            {
                matchedStep = step;
                return true;
            }
        }

        return false;
    }

    private static string ComputeForStep(byte[] secret, long step, int digits)
    {
        Span<byte> counter = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(counter, step);
        var hash = HMACSHA1.HashData(secret, counter);
        var offset = hash[^1] & 0x0F;
        var binary =
            ((hash[offset] & 0x7F) << 24)
            | (hash[offset + 1] << 16)
            | (hash[offset + 2] << 8)
            | hash[offset + 3];
        var modulus = (int)Math.Pow(10, digits);
        return (binary % modulus).ToString($"D{digits}");
    }
}

/// <summary>RFC 4648 Base32 without padding, for authenticator otpauth URIs.</summary>
public static class Base32
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static string Encode(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
            return "";

        var output = new StringBuilder((data.Length * 8 + 4) / 5);
        var buffer = 0;
        var bitsLeft = 0;
        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bitsLeft += 8;
            while (bitsLeft >= 5)
            {
                bitsLeft -= 5;
                output.Append(Alphabet[(buffer >> bitsLeft) & 31]);
            }
        }

        if (bitsLeft > 0)
            output.Append(Alphabet[(buffer << (5 - bitsLeft)) & 31]);

        return output.ToString();
    }

    public static byte[] Decode(string encoded)
    {
        ArgumentNullException.ThrowIfNull(encoded);
        var trimmed = encoded.Trim().TrimEnd('=').ToUpperInvariant();
        var bits = 0;
        var buffer = 0;
        using var ms = new MemoryStream((trimmed.Length * 5) / 8);
        foreach (var c in trimmed)
        {
            var value = Alphabet.IndexOf(c);
            if (value < 0)
                throw new FormatException("Invalid Base32 character.");
            buffer = (buffer << 5) | value;
            bits += 5;
            if (bits >= 8)
            {
                bits -= 8;
                ms.WriteByte((byte)((buffer >> bits) & 0xFF));
            }
        }

        return ms.ToArray();
    }
}
