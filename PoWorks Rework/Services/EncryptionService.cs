using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace PoWorks_Rework.Services
{
    public class EncryptionService
    {
        private readonly byte[] _newKey;
        private readonly string _legacyKeyText;

        public EncryptionService(IConfiguration configuration)
        {
            string configKey = configuration["EncryptionKey"] ?? "PoWorks_SuperSecret_MasterKey_2026!";
            _legacyKeyText = "PoWorks_SecretKey_PcVue_2026_!**";

            using (var sha256 = SHA256.Create())
            {
                _newKey = sha256.ComputeHash(Encoding.UTF8.GetBytes(configKey));
            }
        }

        public string Encrypt(string plainText)
        {
            if (string.IsNullOrEmpty(plainText)) return plainText;

            using (Aes aesAlg = Aes.Create())
            {
                aesAlg.Key = _newKey;
                aesAlg.GenerateIV();

                ICryptoTransform encryptor = aesAlg.CreateEncryptor(aesAlg.Key, aesAlg.IV);

                using (MemoryStream msEncrypt = new MemoryStream())
                {
                    msEncrypt.Write(aesAlg.IV, 0, aesAlg.IV.Length);

                    using (CryptoStream csEncrypt = new CryptoStream(msEncrypt, encryptor, CryptoStreamMode.Write))
                    using (StreamWriter swEncrypt = new StreamWriter(csEncrypt))
                    {
                        swEncrypt.Write(plainText);
                    }

                    return Convert.ToBase64String(msEncrypt.ToArray());
                }
            }
        }

        public string Decrypt(string cipherText)
        {
            if (string.IsNullOrEmpty(cipherText)) return cipherText;

            try
            {
                return DecryptWithKey(cipherText, _newKey);
            }
            catch
            {
                try
                {
                    return DecryptLegacy(cipherText, _legacyKeyText);
                }
                catch
                {
                    return cipherText;
                }
            }
        }

        public bool WasEncryptedWithLegacyKey(string cipherText)
        {
            if (string.IsNullOrEmpty(cipherText)) return false;
            try
            {
                DecryptWithKey(cipherText, _newKey);
                return false;
            }
            catch
            {
                return true;
            }
        }

        private string DecryptWithKey(string cipherText, byte[] key)
        {
            byte[] fullCipher = Convert.FromBase64String(cipherText);

            using (Aes aesAlg = Aes.Create())
            {
                byte[] iv = new byte[16];
                Array.Copy(fullCipher, 0, iv, 0, iv.Length);

                aesAlg.Key = key;
                aesAlg.IV = iv;

                ICryptoTransform decryptor = aesAlg.CreateDecryptor(aesAlg.Key, aesAlg.IV);

                using (MemoryStream msDecrypt = new MemoryStream(fullCipher, iv.Length, fullCipher.Length - iv.Length))
                using (CryptoStream csDecrypt = new CryptoStream(msDecrypt, decryptor, CryptoStreamMode.Read))
                using (StreamReader srDecrypt = new StreamReader(csDecrypt))
                {
                    return srDecrypt.ReadToEnd();
                }
            }
        }

        private string DecryptLegacy(string cipherText, string legacyKeyText)
        {
            byte[] fullCipher = Convert.FromBase64String(cipherText);
            byte[] keyBytes = Encoding.UTF8.GetBytes(legacyKeyText.PadRight(32).Substring(0, 32));

            using (Aes aesAlg = Aes.Create())
            {
                byte[] iv = new byte[16];
                Array.Copy(fullCipher, 0, iv, 0, iv.Length);

                aesAlg.Key = keyBytes;
                aesAlg.IV = iv;

                ICryptoTransform decryptor = aesAlg.CreateDecryptor(aesAlg.Key, aesAlg.IV);

                using (MemoryStream msDecrypt = new MemoryStream(fullCipher, iv.Length, fullCipher.Length - iv.Length))
                using (CryptoStream csDecrypt = new CryptoStream(msDecrypt, decryptor, CryptoStreamMode.Read))
                using (StreamReader srDecrypt = new StreamReader(csDecrypt))
                {
                    return srDecrypt.ReadToEnd();
                }
            }
        }
    }
}