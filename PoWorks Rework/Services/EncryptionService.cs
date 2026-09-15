using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace PoWorks_Rework.Services
{
    /// <summary>
    /// Handles AES encryption and decryption of sensitive values.
    /// New values use a versioned marker so encrypted data can be distinguished
    /// from plain text and cannot be silently encrypted a second time.
    /// </summary>
    public class EncryptionService
    {
        public const string ProtectedValuePrefix = "ENC:v1:";

        private readonly byte[] _newKey;
        private readonly string _legacyKeyText;

        public EncryptionService(IConfiguration configuration)
        {
            string configKey = configuration["EncryptionKey"] ?? "PoWorks_SuperSecret_MasterKey_2026!";
            _legacyKeyText = "PoWorks_SecretKey_PcVue_2026_!**";

            using var sha256 = SHA256.Create();
            _newKey = sha256.ComputeHash(Encoding.UTF8.GetBytes(configKey));
        }

        /// <summary>
        /// Encrypts a plain-text value using the current key and adds a versioned marker.
        /// Passing an already valid current-format value is idempotent.
        /// </summary>
        public string Encrypt(string plainText)
        {
            if (string.IsNullOrEmpty(plainText))
                return plainText;

            if (plainText.StartsWith(ProtectedValuePrefix, StringComparison.Ordinal))
            {
                if (TryDecryptStoredValue(plainText, out _, out _))
                    return plainText;

                throw new CryptographicException(
                    "The value is marked as encrypted but cannot be decrypted with the configured EncryptionKey.");
            }

            return ProtectedValuePrefix + EncryptWithKey(plainText, _newKey);
        }

        /// <summary>
        /// Decrypts current-format, previous current-key, or legacy-key values.
        /// Plain text and values that cannot be decrypted are returned unchanged
        /// for backwards compatibility with existing configuration flows.
        /// </summary>
        public string Decrypt(string cipherText)
        {
            if (string.IsNullOrEmpty(cipherText))
                return cipherText;

            return TryDecryptStoredValue(cipherText, out var plainText, out _)
                ? plainText
                : cipherText;
        }

        /// <summary>
        /// Normalizes a stored secret into the current ENC:v1 format.
        /// It refuses to wrap data that looks like historical AES ciphertext but
        /// cannot be decrypted with either the current or legacy key. This prevents
        /// a key mismatch from silently double-encrypting credentials.
        /// </summary>
        public string NormalizeForStorage(string value)
        {
            if (string.IsNullOrEmpty(value))
                return value;

            if (value.StartsWith(ProtectedValuePrefix, StringComparison.Ordinal))
            {
                if (TryDecryptStoredValue(value, out _, out _))
                    return value;

                throw new CryptographicException(
                    "Stored credential uses ENC:v1 but cannot be decrypted with the configured EncryptionKey.");
            }

            if (TryDecryptStoredValue(value, out var decrypted, out var wasEncrypted) && wasEncrypted)
                return Encrypt(decrypted);

            if (LooksLikeHistoricalCiphertext(value))
            {
                throw new CryptographicException(
                    "Stored credential looks encrypted but cannot be decrypted. The EncryptionKey may not match the key used to create it.");
            }

            return Encrypt(value);
        }

        /// <summary>
        /// Attempts to decrypt a value and reports whether it was recognized as encrypted.
        /// </summary>
        public bool TryDecryptStoredValue(string value, out string plainText, out bool wasEncrypted)
        {
            plainText = value;
            wasEncrypted = false;

            if (string.IsNullOrEmpty(value))
                return true;

            if (value.StartsWith(ProtectedValuePrefix, StringComparison.Ordinal))
            {
                wasEncrypted = true;
                var payload = value.Substring(ProtectedValuePrefix.Length);
                try
                {
                    plainText = DecryptWithKey(payload, _newKey);
                    return true;
                }
                catch
                {
                    plainText = value;
                    return false;
                }
            }

            try
            {
                plainText = DecryptWithKey(value, _newKey);
                wasEncrypted = true;
                return true;
            }
            catch
            {
                try
                {
                    plainText = DecryptLegacy(value, _legacyKeyText);
                    wasEncrypted = true;
                    return true;
                }
                catch
                {
                    plainText = value;
                    wasEncrypted = false;
                    return false;
                }
            }
        }

        /// <summary>
        /// Checks whether an unmarked value can specifically be decrypted by the legacy key.
        /// </summary>
        public bool WasEncryptedWithLegacyKey(string cipherText)
        {
            if (string.IsNullOrEmpty(cipherText) ||
                cipherText.StartsWith(ProtectedValuePrefix, StringComparison.Ordinal))
            {
                return false;
            }

            try
            {
                DecryptWithKey(cipherText, _newKey);
                return false;
            }
            catch
            {
                try
                {
                    DecryptLegacy(cipherText, _legacyKeyText);
                    return true;
                }
                catch
                {
                    return false;
                }
            }
        }

        private static bool LooksLikeHistoricalCiphertext(string value)
        {
            try
            {
                var bytes = Convert.FromBase64String(value);
                return bytes.Length >= 32 && (bytes.Length - 16) % 16 == 0;
            }
            catch
            {
                return false;
            }
        }

        private static string EncryptWithKey(string plainText, byte[] key)
        {
            using var aesAlg = Aes.Create();
            aesAlg.Key = key;
            aesAlg.GenerateIV();

            using var msEncrypt = new MemoryStream();
            msEncrypt.Write(aesAlg.IV, 0, aesAlg.IV.Length);

            using (var csEncrypt = new CryptoStream(
                msEncrypt,
                aesAlg.CreateEncryptor(aesAlg.Key, aesAlg.IV),
                CryptoStreamMode.Write))
            using (var swEncrypt = new StreamWriter(csEncrypt))
            {
                swEncrypt.Write(plainText);
            }

            return Convert.ToBase64String(msEncrypt.ToArray());
        }

        private static string DecryptWithKey(string cipherText, byte[] key)
        {
            byte[] fullCipher = Convert.FromBase64String(cipherText);
            if (fullCipher.Length <= 16)
                throw new CryptographicException("Ciphertext is too short.");

            using var aesAlg = Aes.Create();
            byte[] iv = new byte[16];
            Array.Copy(fullCipher, 0, iv, 0, iv.Length);

            aesAlg.Key = key;
            aesAlg.IV = iv;

            using var msDecrypt = new MemoryStream(fullCipher, iv.Length, fullCipher.Length - iv.Length);
            using var csDecrypt = new CryptoStream(
                msDecrypt,
                aesAlg.CreateDecryptor(aesAlg.Key, aesAlg.IV),
                CryptoStreamMode.Read);
            using var srDecrypt = new StreamReader(csDecrypt);
            return srDecrypt.ReadToEnd();
        }

        private static string DecryptLegacy(string cipherText, string legacyKeyText)
        {
            byte[] keyBytes = Encoding.UTF8.GetBytes(legacyKeyText.PadRight(32).Substring(0, 32));
            return DecryptWithKey(cipherText, keyBytes);
        }
    }
}
