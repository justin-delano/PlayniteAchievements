using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace PlayniteAchievements.Common
{
    /// <summary>
    /// Provides AES encryption and decryption for sensitive data storage.
    /// </summary>
    public static class Encryption
    {
        /// <summary>
        /// Generates a cryptographically secure random salt.
        /// </summary>
        public static byte[] GenerateRandomSalt()
        {
            byte[] data = new byte[32];
            using (var rng = new RNGCryptoServiceProvider())
            {
                for (int i = 0; i < 10; i++)
                {
                    rng.GetBytes(data);
                }
            }
            return data;
        }

        /// <summary>
        /// Encrypts content and writes it to a file.
        /// </summary>
        public static void EncryptToFile(string filePath, string content, Encoding encoding, string password)
        {
            byte[] salt = GenerateRandomSalt();
            using (var outFile = new FileStream(filePath, FileMode.Create))
            {
                var passwordBytes = Encoding.UTF8.GetBytes(password);
                using (var AES = new RijndaelManaged())
                {
                    AES.KeySize = 256;
                    AES.BlockSize = 128;
                    AES.Padding = PaddingMode.PKCS7;
                    AES.Mode = CipherMode.CFB;
                    using (var key = new Rfc2898DeriveBytes(passwordBytes, salt, 1000))
                    {
                        AES.Key = key.GetBytes(AES.KeySize / 8);
                        AES.IV = key.GetBytes(AES.BlockSize / 8);
                    }

                    outFile.Write(salt, 0, salt.Length);
                    using (var cs = new CryptoStream(outFile, AES.CreateEncryptor(), CryptoStreamMode.Write))
                    {
                        var byteContent = encoding.GetBytes(content);
                        cs.Write(byteContent, 0, byteContent.Length);
                        cs.FlushFinalBlock();
                        cs.Close();
                        outFile.Close();
                    }
                }
            }
        }

        /// <summary>
        /// Decrypts content from a file.
        /// Reads the entire file into memory first to avoid file handle conflicts.
        /// </summary>
        public static string DecryptFromFile(string inputFile, Encoding encoding, string password)
        {
            byte[] encryptedData = File.ReadAllBytes(inputFile);
            var passwordBytes = Encoding.UTF8.GetBytes(password);
            var salt = new byte[32];

            Array.Copy(encryptedData, 0, salt, 0, salt.Length);

            using (var AES = new RijndaelManaged())
            {
                AES.KeySize = 256;
                AES.BlockSize = 128;
                AES.Padding = PaddingMode.PKCS7;
                AES.Mode = CipherMode.CFB;

                using (var key = new Rfc2898DeriveBytes(passwordBytes, salt, 1000))
                {
                    AES.Key = key.GetBytes(AES.KeySize / 8);
                    AES.IV = key.GetBytes(AES.BlockSize / 8);
                }

                using (var memoryStream = new MemoryStream(encryptedData, 32, encryptedData.Length - 32))
                using (var cs = new CryptoStream(memoryStream, AES.CreateDecryptor(), CryptoStreamMode.Read))
                using (var reader = new StreamReader(cs, encoding))
                {
                    return reader.ReadToEnd();
                }
            }
        }
    }
}
