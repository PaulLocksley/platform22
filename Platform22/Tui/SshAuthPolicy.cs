namespace Platform22.Tui;

using System.Security.Cryptography;
using FxSsh.Services;

/// <summary>
/// SSH authentication policy for Platform22 sessions.
///
/// Modes (PLATFORM22_SSH_AUTH):
///   none      - accept every session. Local development only.
///   publickey - accept only clients whose public key is in the authorized set.
///   (unset)   - publickey: fails closed; an unconfigured deployment accepts nobody.
///
/// The Aspire AppHost sets "none" in run mode so local development keeps
/// zero-config anonymous SSH. Keys come from PLATFORM22_SSH_AUTHORIZED_KEYS
/// (newline or comma separated) or PLATFORM22_SSH_AUTHORIZED_KEYS_FILE (one
/// entry per line, e.g. a k8s Secret mount). Entries may be full
/// authorized_keys lines or bare fingerprints in "SHA256:&lt;base64&gt;" or
/// colon-separated MD5 form.
/// </summary>
public sealed class SshAuthPolicy
{
    public const string AuthModeVariable = "PLATFORM22_SSH_AUTH";
    public const string AuthorizedKeysVariable = "PLATFORM22_SSH_AUTHORIZED_KEYS";
    public const string AuthorizedKeysFileVariable = "PLATFORM22_SSH_AUTHORIZED_KEYS_FILE";
    public const string HostKeyPathVariable = "PLATFORM22_SSH_HOST_KEY_PATH";

    private const string NoneAuthMethod = "none";
    private const string PublicKeyAuthMethod = "publickey";

    private readonly bool allowAll;
    private readonly HashSet<string> acceptedFingerprints;
    private readonly int acceptedKeyEntries;

    private SshAuthPolicy(bool allowAll, HashSet<string> acceptedFingerprints, int acceptedKeyEntries)
    {
        this.allowAll = allowAll;
        this.acceptedFingerprints = acceptedFingerprints;
        this.acceptedKeyEntries = acceptedKeyEntries;
    }

    /// <summary>True when the policy accepts unauthenticated sessions.</summary>
    public bool AllowNone => allowAll;

    /// <summary>Number of authorized key entries. Zero when anonymous access is on.</summary>
    public int AcceptedKeyCount => acceptedKeyEntries;

    /// <summary>Builds the policy from PLATFORM22_SSH_* environment variables.</summary>
    public static SshAuthPolicy FromEnvironment()
    {
        var mode = Environment.GetEnvironmentVariable(AuthModeVariable);
        var keys = LoadAuthorizedKeys();
        return Create(mode, keys);
    }

    /// <summary>
    /// Resolves the effective policy. Explicit modes win; unset fails closed
    /// to publickey so an unconfigured deployment accepts nobody.
    /// </summary>
    public static SshAuthPolicy Create(string? mode, IEnumerable<string> authorizedKeyEntries)
    {
        var entries = authorizedKeyEntries
            .SelectMany(entry => entry.Split(['\n', '\r', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Where(entry => entry.Length > 0)
            .ToArray();

        var allowAll = mode?.ToLowerInvariant() switch
        {
            "none" => true,
            _ => false
        };

        var fingerprints = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            AddEntry(fingerprints, entry);
        }

        return new SshAuthPolicy(allowAll, fingerprints, entries.Length);
    }

    /// <summary>Wires the policy into an incoming FxSsh user-auth service.</summary>
    public void Configure(UserAuthService userAuthService)
    {
        if (allowAll)
        {
            // WARNING: anonymous access. Only acceptable for local development.
            userAuthService.EnableNoneAuth = true;
            userAuthService.UserAuth += (_, args) => args.Result = true;
            return;
        }

        userAuthService.UserAuth += (_, args) => args.Result = Evaluate(args.AuthMethod, args.Key);
    }

    /// <summary>Decision core, separate from FxSsh so tests need no SSH server.</summary>
    public bool Evaluate(string? authMethod, byte[]? key)
    {
        if (allowAll)
        {
            return true;
        }

        if (!string.Equals(authMethod, PublicKeyAuthMethod, StringComparison.OrdinalIgnoreCase) || key is null || key.Length == 0)
        {
            return false;
        }

        return acceptedFingerprints.Contains(FingerprintSha256(key))
            || acceptedFingerprints.Contains(FingerprintMd5(key));
    }

    internal static IReadOnlyCollection<string> LoadAuthorizedKeys()
    {
        var inline = Environment.GetEnvironmentVariable(AuthorizedKeysVariable);
        if (!string.IsNullOrWhiteSpace(inline))
        {
            return [inline];
        }

        var filePath = Environment.GetEnvironmentVariable(AuthorizedKeysFileVariable);
        filePath = string.IsNullOrWhiteSpace(filePath)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "platform22", "authorized_keys")
            : filePath;
        return File.Exists(filePath) ? [File.ReadAllText(filePath)] : [];
    }

    private static void AddEntry(HashSet<string> fingerprints, string entry)
    {
        var parts = entry.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        // Full authorized_keys line: "<type> <base64-blob> [comment]".
        if (parts.Length >= 2 && !parts[0].Contains(':'))
        {
            try
            {
                var blob = Convert.FromBase64String(parts[1]);
                fingerprints.Add(FingerprintSha256(blob));
                fingerprints.Add(FingerprintMd5(blob));
                return;
            }
            catch (FormatException)
            {
                // Fall through to raw-fingerprint handling.
            }
        }

        fingerprints.Add(NormalizeFingerprint(entry));
    }

    private static string NormalizeFingerprint(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.StartsWith("SHA256:", StringComparison.OrdinalIgnoreCase))
        {
            return "SHA256:" + trimmed["SHA256:".Length..].TrimEnd('=');
        }

        return trimmed.ToLowerInvariant();
    }

    internal static string FingerprintSha256(byte[] key)
    {
        var digest = SHA256.HashData(key);
        return "SHA256:" + Convert.ToBase64String(digest).TrimEnd('=');
    }

    internal static string FingerprintMd5(byte[] key)
    {
        var digest = MD5.HashData(key);
        return string.Join(':', digest.Select(value => value.ToString("x2")));
    }
}
