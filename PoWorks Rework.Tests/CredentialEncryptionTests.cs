using Microsoft.Extensions.Configuration;
using PoWorks_Rework.Services;
using System.Security.Cryptography;
using Xunit;

namespace PoWorks_Rework.Tests;

public class CredentialEncryptionTests
{
    [Fact]
    public void Encrypt_UsesVersionedFormat_AndRoundTrips()
    {
        var service = CreateService("key-a");
        var encrypted = service.Encrypt("postgres-password");

        Assert.StartsWith(EncryptionService.ProtectedValuePrefix, encrypted);
        Assert.Equal("postgres-password", service.Decrypt(encrypted));
    }

    [Fact]
    public void Encrypt_IsIdempotent_ForAlreadyProtectedValue()
    {
        var service = CreateService("key-a");
        var encrypted = service.Encrypt("secret");

        Assert.Equal(encrypted, service.Encrypt(encrypted));
    }

    [Fact]
    public void PreviousUnmarkedCurrentKeyCiphertext_IsStillReadableAndMigrates()
    {
        var service = CreateService("key-a");
        var marked = service.Encrypt("secret");
        var oldStyle = marked.Substring(EncryptionService.ProtectedValuePrefix.Length);

        Assert.Equal("secret", service.Decrypt(oldStyle));

        var normalized = service.NormalizeForStorage(oldStyle);
        Assert.StartsWith(EncryptionService.ProtectedValuePrefix, normalized);
        Assert.Equal("secret", service.Decrypt(normalized));
    }

    [Fact]
    public void NormalizeForStorage_EncryptsPlainTextOnce()
    {
        var service = CreateService("key-a");

        var normalized = service.NormalizeForStorage("plain-password");

        Assert.StartsWith(EncryptionService.ProtectedValuePrefix, normalized);
        Assert.Equal("plain-password", service.Decrypt(normalized));
        Assert.Equal(normalized, service.NormalizeForStorage(normalized));
    }

    [Fact]
    public void NormalizeForStorage_DoesNotDoubleEncryptCiphertextFromUnknownKey()
    {
        var source = CreateService("key-a");
        var differentKey = CreateService("key-b");

        var marked = source.Encrypt("secret");
        var historicalCiphertext = marked.Substring(EncryptionService.ProtectedValuePrefix.Length);

        Assert.Throws<CryptographicException>(
            () => differentKey.NormalizeForStorage(historicalCiphertext));
    }

    [Fact]
    public void NormalizeForStorage_RejectsMarkedValueFromUnknownKey()
    {
        var source = CreateService("key-a");
        var differentKey = CreateService("key-b");
        var encrypted = source.Encrypt("secret");

        Assert.Throws<CryptographicException>(
            () => differentKey.NormalizeForStorage(encrypted));
    }

    private static EncryptionService CreateService(string key)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["EncryptionKey"] = key
            })
            .Build();

        return new EncryptionService(configuration);
    }
}
