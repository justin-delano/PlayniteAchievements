using Playnite.SDK;
using PlayniteAchievements.Providers.RetroAchievements.Hashing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PlayniteAchievements.Providers.RetroAchievements.Hashing.Hashers
{
    internal sealed class Ps2CustomHasher : DiscBasedHasher
    {
        public Ps2CustomHasher(ILogger logger) : base(logger) { }

        public override string Name => "PlayStation 2 (SYSTEM.CNF BOOT2 + executable MD5)";

        protected override async Task<IReadOnlyList<string>> ComputeHashesInternalAsync(string filePath, CancellationToken cancel)
        {
            using (var iso = new DiscUtilsFacade(filePath))
            {
                var exeName = await FindBootExecutableAsync(iso, bootKey: "BOOT2", cdromPrefix: "cdrom0:", cancel).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(exeName))
                {
                    Logger?.Warn($"[RA] {Name}: Could not locate primary executable via SYSTEM.CNF: {filePath}");
                    return Array.Empty<string>();
                }

                using (var exeStream = iso.OpenFileOrNull(exeName))
                {
                    if (exeStream == null)
                    {
                        Logger?.Warn($"[RA] {Name}: Could not locate primary executable '{exeName}': {filePath}");
                        return Array.Empty<string>();
                    }

                    var maxBytes = Math.Min(HashUtils.MaxHashBytes, exeStream.Length);

                    using (var md5 = MD5.Create())
                    {
                        var exeNameBytes = Encoding.ASCII.GetBytes(exeName);
                        md5.TransformBlock(exeNameBytes, 0, exeNameBytes.Length, null, 0);

                        await HashUtils.AppendStreamAsync(md5, exeStream, maxBytes, cancel).ConfigureAwait(false);

                        md5.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                        return new[] { HashUtils.ToHexLower(md5.Hash) };
                    }
                }
            }
        }

        private static async Task<string> FindBootExecutableAsync(DiscUtilsFacade iso, string bootKey, string cdromPrefix, CancellationToken cancel)
        {
            using (var cnfStream = iso.OpenFileOrNull("SYSTEM.CNF"))
            {
                if (cnfStream == null)
                {
                    return null;
                }

                using (var reader = new StreamReader(cnfStream, Encoding.ASCII, detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true))
                {
                    var content = await reader.ReadToEndAsync().ConfigureAwait(false);
                    cancel.ThrowIfCancellationRequested();

                    var lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var raw in lines)
                    {
                        var line = raw.Trim();
                        if (!line.StartsWith(bootKey, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        var rest = line.Substring(bootKey.Length).TrimStart();
                        if (!rest.StartsWith("=", StringComparison.Ordinal))
                        {
                            continue;
                        }

                        rest = rest.Substring(1).TrimStart();
                        if (rest.StartsWith(cdromPrefix, StringComparison.OrdinalIgnoreCase))
                        {
                            rest = rest.Substring(cdromPrefix.Length);
                        }

                        while (rest.StartsWith("\\", StringComparison.Ordinal))
                        {
                            rest = rest.Substring(1);
                        }

                        var end = rest.IndexOfAny(new[] { ' ', '\t', ';' });
                        if (end >= 0)
                        {
                            rest = rest.Substring(0, end);
                        }

                        return string.IsNullOrWhiteSpace(rest) ? null : rest;
                    }
                }
            }

            return null;
        }
    }
}

