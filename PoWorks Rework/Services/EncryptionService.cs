using System.Security.Cryptography;
using System.Text;

namespace PoWorks_Rework.Services
{
    public class EncryptionService
    {
        // Clé secrète maître (doit faire exactement 32 caractères pour l'AES-256).
        // Dans le futur, on pourra la déplacer dans le appsettings.json.
        private readonly string _key = "PoWorks_SecretKey_PcVue_2026_!**";

        public string Encrypt(string clearText)
        {
            if (string.IsNullOrEmpty(clearText)) return clearText;

            byte[] clearBytes = Encoding.UTF8.GetBytes(clearText);
            using Aes aes = Aes.Create();
            aes.Key = Encoding.UTF8.GetBytes(_key);
            aes.GenerateIV(); // Génère un vecteur d'initialisation unique

            using MemoryStream ms = new MemoryStream();
            // On sauvegarde l'IV au tout début du message chiffré
            ms.Write(aes.IV, 0, aes.IV.Length);

            using CryptoStream cs = new CryptoStream(ms, aes.CreateEncryptor(), CryptoStreamMode.Write);
            cs.Write(clearBytes, 0, clearBytes.Length);
            cs.FlushFinalBlock();

            return Convert.ToBase64String(ms.ToArray());
        }

        public string Decrypt(string cipherText)
        {
            if (string.IsNullOrEmpty(cipherText)) return cipherText;

            try
            {
                byte[] cipherBytes = Convert.FromBase64String(cipherText);
                using Aes aes = Aes.Create();
                aes.Key = Encoding.UTF8.GetBytes(_key);

                // Extraction de l'IV (les 16 premiers octets)
                byte[] iv = new byte[aes.BlockSize / 8];
                Array.Copy(cipherBytes, 0, iv, 0, iv.Length);
                aes.IV = iv;

                using MemoryStream ms = new MemoryStream();
                using CryptoStream cs = new CryptoStream(ms, aes.CreateDecryptor(), CryptoStreamMode.Write);
                // On déchiffre le reste du message (après l'IV)
                cs.Write(cipherBytes, iv.Length, cipherBytes.Length - iv.Length);
                cs.FlushFinalBlock();

                return Encoding.UTF8.GetString(ms.ToArray());
            }
            catch
            {
                // Si la chaîne n'est pas du base64 valide (ex: un vieux mot de passe en clair dans la BDD),
                // on la retourne telle quelle pour ne pas casser le système.
                return cipherText;
            }
        }
    }
}