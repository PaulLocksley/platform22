namespace Platform22.Tests;

using Platform22.Tui;
using Xunit;

public sealed class SshAuthPolicyTests
{
    [Fact]
    public void UnsetMode_FailsClosed()
    {
        var policy = SshAuthPolicy.Create(mode: null, []);

        Assert.False(policy.AllowNone);
        Assert.False(policy.Evaluate("none", null));
        Assert.False(policy.Evaluate("publickey", [1, 2, 3]));
    }

    [Fact]
    public void NoneMode_AcceptsEverything()
    {
        var policy = SshAuthPolicy.Create("none", []);

        Assert.True(policy.AllowNone);
        Assert.True(policy.Evaluate("none", null));
        Assert.True(policy.Evaluate("password", "secret"u8.ToArray()));
    }

    [Fact]
    public void PublicKeyMode_RejectsPasswordsAndUnknownKeys()
    {
        var key = TestKeyBytes(1);
        var policy = SshAuthPolicy.Create("publickey", [$"ssh-ed25519 {Convert.ToBase64String(key)} test@laptop"]);

        Assert.False(policy.Evaluate("password", "secret"u8.ToArray()));
        Assert.False(policy.Evaluate("none", null));
        Assert.False(policy.Evaluate("publickey", TestKeyBytes(2)));
        Assert.True(policy.Evaluate("publickey", key));
        Assert.Equal(1, policy.AcceptedKeyCount);
    }

    [Fact]
    public void PublicKeyMode_AcceptsSha256FingerprintEntries()
    {
        var key = TestKeyBytes(3);
        var fingerprint = Fingerprint(key);
        var policy = SshAuthPolicy.Create("publickey", [$"SHA256:{fingerprint}"]);

        Assert.True(policy.Evaluate("publickey", key));
    }

    [Theory]
    [InlineData("ssh-ed25519 AAAA test@laptop\nssh-ed25519 BBBB other@laptop")]
    [InlineData("ssh-ed25519 AAAA test@laptop,ssh-ed25519 BBBB other@laptop")]
    public void MultipleEntries_ParseFromNewlinesAndCommas(string entries)
    {
        var policy = SshAuthPolicy.Create("publickey", [entries]);

        Assert.Equal(2, policy.AcceptedKeyCount);
    }

    private static byte[] TestKeyBytes(byte seed)
    {
        return Enumerable.Range(0, 32).Select(i => (byte)(i + seed)).ToArray();
    }

    private static string Fingerprint(byte[] key)
    {
        // Mirrors the OpenSSH SHA256 fingerprint: base64 without padding.
        using var sha = System.Security.Cryptography.SHA256.Create();
        return Convert.ToBase64String(sha.ComputeHash(key)).TrimEnd('=');
    }
}
