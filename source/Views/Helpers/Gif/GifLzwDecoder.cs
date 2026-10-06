namespace PlayniteAchievements.Views.Helpers.Gif
{
    /// <summary>
    /// Decodes a frame's LZW sub-blocks straight from the payload into a caller-owned index
    /// buffer. The code tables are allocated once per instance and reused for every frame.
    /// </summary>
    internal sealed class GifLzwDecoder
    {
        private const int MaxCodes = 4096;

        private readonly short[] _prefix = new short[MaxCodes];
        private readonly byte[] _suffix = new byte[MaxCodes];
        private readonly byte[] _stack = new byte[MaxCodes + 1];

        /// <summary>
        /// Writes up to <paramref name="pixelCount"/> palette indices into <paramref name="output"/>
        /// and returns how many were written. A corrupt or truncated stream stops early instead of
        /// throwing; the pixels it did produce are still valid.
        /// </summary>
        internal int Decode(byte[] data, int offset, int minimumCodeSize, byte[] output, int pixelCount)
        {
            if (minimumCodeSize < 1 || minimumCodeSize > 11 || pixelCount <= 0)
            {
                return 0;
            }

            var prefix = _prefix;
            var suffix = _suffix;
            var stack = _stack;

            var clear = 1 << minimumCodeSize;
            var endOfInformation = clear + 1;
            var available = clear + 2;
            var codeSize = minimumCodeSize + 1;
            var codeMask = (1 << codeSize) - 1;
            var oldCode = -1;
            var first = 0;

            for (var code = 0; code < clear; code++)
            {
                prefix[code] = 0;
                suffix[code] = (byte)code;
            }

            var position = offset;
            var blockRemaining = 0;
            var datum = 0;
            var bits = 0;
            var top = 0;
            var written = 0;

            while (written < pixelCount)
            {
                if (top > 0)
                {
                    output[written++] = stack[--top];
                    continue;
                }

                while (bits < codeSize)
                {
                    if (blockRemaining == 0)
                    {
                        if (position >= data.Length)
                        {
                            return written;
                        }

                        blockRemaining = data[position++];
                        if (blockRemaining == 0)
                        {
                            return written;
                        }
                    }

                    if (position >= data.Length)
                    {
                        return written;
                    }

                    datum |= data[position++] << bits;
                    bits += 8;
                    blockRemaining--;
                }

                var current = datum & codeMask;
                datum >>= codeSize;
                bits -= codeSize;

                if (current == clear)
                {
                    codeSize = minimumCodeSize + 1;
                    codeMask = (1 << codeSize) - 1;
                    available = clear + 2;
                    oldCode = -1;
                    continue;
                }

                if (current == endOfInformation)
                {
                    return written;
                }

                if (oldCode == -1)
                {
                    if (current >= clear)
                    {
                        return written;
                    }

                    output[written++] = suffix[current];
                    oldCode = current;
                    first = current;
                    continue;
                }

                if (current > available)
                {
                    return written;
                }

                var incoming = current;
                if (current == available)
                {
                    stack[top++] = (byte)first;
                    current = oldCode;
                }

                while (current > clear)
                {
                    stack[top++] = suffix[current];
                    current = prefix[current];
                }

                first = suffix[current];
                stack[top++] = (byte)first;

                if (available < MaxCodes)
                {
                    prefix[available] = (short)oldCode;
                    suffix[available] = (byte)first;
                    available++;
                    if (available == codeMask + 1 && codeSize < 12)
                    {
                        codeSize++;
                        codeMask = (1 << codeSize) - 1;
                    }
                }

                oldCode = incoming;
            }

            return written;
        }
    }
}
