using System;
using System.IO;
using System.Runtime.InteropServices;
using PlayniteAchievements.Services.Capture;
using SharpDX;
using SharpDX.MediaFoundation;

namespace PlayniteAchievements.Services.Images.Webm
{
    /// <summary>
    /// Decodes one VP8 or VP9 bitstream through the decoder Windows provides (a synchronous Media
    /// Foundation transform) into NV12, or P010 for a 10-bit stream. Frames go in one at a time in decode order and come out on
    /// the same call; VP8 and VP9 have no frame reordering. Not thread-safe: one owner at a time.
    /// </summary>
    internal sealed class WebmVideoDecoder : IDisposable
    {
        private const int StreamChangeHResult = unchecked((int)0xC00D6D61);
        private const int ProvidesSamplesFlags = 0x100 | 0x200;

        private static readonly Guid Nv12 = FourCcGuid("NV12");
        private static readonly Guid P010 = FourCcGuid("P010");

        private readonly IDisposable _runtime;
        private readonly Transform _transform;
        private bool _providesSamples;
        private int _outputBufferSize;
        private long _sampleTime;

        internal WebmVideoDecoder(WebmCodec codec, int width, int height)
        {
            _runtime = MediaFoundationRuntime.Acquire();
            try
            {
                _transform = CreateTransform(codec);
                using (var input = new MediaType())
                {
                    input.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video);
                    input.Set(MediaTypeAttributeKeys.Subtype, SubtypeFor(codec));
                    input.Set(MediaTypeAttributeKeys.FrameSize, ((long)width << 32) | (uint)height);
                    _transform.SetInputType(0, input, 0);
                }

                SelectOutputType();
            }
            catch
            {
                _transform?.Dispose();
                _runtime.Dispose();
                throw;
            }
        }

        /// <summary>Width of the decoded planes, which may be padded past the frame's own width.</summary>
        internal int PlaneWidth { get; private set; }

        internal int PlaneHeight { get; private set; }

        /// <summary>Bytes per row of the Y plane and of the interleaved UV plane.</summary>
        internal int Stride { get; private set; }

        /// <summary>
        /// True for a 10-bit stream (VP9 profile 2), which the decoder only offers as P010: 16-bit
        /// little-endian samples with the value in the top 10 bits. Otherwise samples are NV12 bytes.
        /// </summary>
        internal bool IsTenBit { get; private set; }

        /// <summary>The last decoded frame: Y plane, then the half-height interleaved UV plane.</summary>
        internal byte[] Planes { get; private set; } = new byte[0];

        /// <summary>True when Windows has a decoder for <paramref name="codec"/>.</summary>
        internal static bool IsAvailable(WebmCodec codec)
        {
            using (MediaFoundationRuntime.Acquire())
            {
                var activates = FindDecoders(codec);
                foreach (var activate in activates)
                {
                    activate.Dispose();
                }

                return activates.Length > 0;
            }
        }

        /// <summary>Decodes one frame into <see cref="Planes"/>.</summary>
        internal void Decode(byte[] payload, int offset, int length)
        {
            using (var buffer = MediaFactory.CreateMemoryBuffer(length))
            {
                var pointer = buffer.Lock(out _, out _);
                Marshal.Copy(payload, offset, pointer, length);
                buffer.Unlock();
                buffer.CurrentLength = length;

                using (var sample = MediaFactory.CreateSample())
                {
                    sample.AddBuffer(buffer);

                    // The decoder only needs increasing timestamps; display timing comes from the container.
                    sample.SampleTime = _sampleTime;
                    sample.SampleDuration = 1;
                    _sampleTime++;
                    _transform.ProcessInput(0, sample, 0);
                }
            }

            if (!TryReadOutput() && !Drain())
            {
                throw new InvalidDataException("The WebM decoder produced no frame.");
            }
        }

        /// <summary>Discards decoder state so the next frame decodes as a fresh start (a keyframe).</summary>
        internal void Reset()
        {
            _transform.ProcessMessage(TMessageType.CommandFlush, IntPtr.Zero);
        }

        private bool Drain()
        {
            _transform.ProcessMessage(TMessageType.CommandDrain, IntPtr.Zero);
            return TryReadOutput();
        }

        private bool TryReadOutput()
        {
            while (true)
            {
                var output = new TOutputDataBuffer[1];
                Sample allocated = null;
                if (!_providesSamples)
                {
                    allocated = MediaFactory.CreateSample();
                    using (var buffer = MediaFactory.CreateMemoryBuffer(_outputBufferSize))
                    {
                        allocated.AddBuffer(buffer);
                    }

                    output[0].PSample = allocated;
                }

                bool produced;
                try
                {
                    produced = _transform.ProcessOutput(TransformProcessOutputFlags.None, output, out _);
                }
                catch (SharpDXException ex) when (ex.ResultCode.Code == StreamChangeHResult)
                {
                    // The real frame size is known once the first frame is parsed (odd sizes are
                    // padded to even), so the decoder renegotiates and the frame is read again.
                    allocated?.Dispose();
                    SelectOutputType();
                    continue;
                }

                if (!produced)
                {
                    allocated?.Dispose();
                    return false;
                }

                var result = output[0].PSample;
                try
                {
                    CopyFrame(result);
                }
                finally
                {
                    result.Dispose();
                    output[0].PEvents?.Dispose();
                    if (allocated != null && !ReferenceEquals(allocated, result))
                    {
                        allocated.Dispose();
                    }
                }

                return true;
            }
        }

        private void CopyFrame(Sample sample)
        {
            using (var buffer = sample.ConvertToContiguousBuffer())
            {
                var pointer = buffer.Lock(out _, out var length);
                try
                {
                    var expected = Stride * PlaneHeight * 3 / 2;
                    if (length < expected)
                    {
                        throw new InvalidDataException("The WebM decoder returned a short frame.");
                    }

                    if (Planes.Length != expected)
                    {
                        Planes = new byte[expected];
                    }

                    Marshal.Copy(pointer, Planes, 0, expected);
                }
                finally
                {
                    buffer.Unlock();
                }
            }
        }

        /// <summary>
        /// NV12 for 8-bit streams. A 10-bit stream offers only P010 once its first frame is parsed,
        /// and keeping the extra precision is what lets smooth gradients convert without banding.
        /// </summary>
        private void SelectOutputType()
        {
            if (!TrySelectOutputType(Nv12) && !TrySelectOutputType(P010))
            {
                throw new InvalidDataException("The WebM decoder offers neither NV12 nor P010 output.");
            }
        }

        private bool TrySelectOutputType(Guid subtype)
        {
            for (var index = 0; _transform.TryGetOutputAvailableType(0, index, out var candidate); index++)
            {
                using (candidate)
                {
                    if (candidate.Get(MediaTypeAttributeKeys.Subtype) != subtype)
                    {
                        continue;
                    }

                    _transform.SetOutputType(0, candidate, 0);
                    IsTenBit = subtype == P010;
                    var frameSize = candidate.Get(MediaTypeAttributeKeys.FrameSize);
                    PlaneWidth = (int)(frameSize >> 32);
                    PlaneHeight = (int)(frameSize & 0xFFFFFFFF);
                    Stride = ReadStride(candidate, PlaneWidth * (IsTenBit ? 2 : 1));
                    _transform.GetOutputStreamInfo(0, out var info);
                    _providesSamples = (info.DwFlags & ProvidesSamplesFlags) != 0;
                    _outputBufferSize = Math.Max(info.CbSize, Stride * PlaneHeight * 3 / 2);
                    return true;
                }
            }

            return false;
        }

        private static int ReadStride(MediaType type, int fallback)
        {
            try
            {
                var stride = type.Get(MediaTypeAttributeKeys.DefaultStride);
                return stride >= fallback ? stride : fallback;
            }
            catch (SharpDXException)
            {
                return fallback;
            }
        }

        private static Transform CreateTransform(WebmCodec codec)
        {
            var activates = FindDecoders(codec);
            try
            {
                if (activates.Length == 0)
                {
                    throw new NotSupportedException($"Windows has no {codec} decoder installed.");
                }

                return activates[0].ActivateObject<Transform>();
            }
            finally
            {
                foreach (var activate in activates)
                {
                    activate.Dispose();
                }
            }
        }

        private static Activate[] FindDecoders(WebmCodec codec)
        {
            return MediaFactory.FindTransform(
                TransformCategoryGuids.VideoDecoder,
                TransformEnumFlag.Syncmft | TransformEnumFlag.Localmft | TransformEnumFlag.SortAndFilter,
                new TRegisterTypeInformation { GuidMajorType = MediaTypeGuids.Video, GuidSubtype = SubtypeFor(codec) },
                null) ?? new Activate[0];
        }

        private static Guid SubtypeFor(WebmCodec codec)
        {
            return FourCcGuid(codec == WebmCodec.Vp8 ? "VP80" : "VP90");
        }

        private static Guid FourCcGuid(string fourCc)
        {
            var code = fourCc[0] | (fourCc[1] << 8) | (fourCc[2] << 16) | (fourCc[3] << 24);
            return new Guid(code, 0x0000, 0x0010, 0x80, 0x00, 0x00, 0xAA, 0x00, 0x38, 0x9B, 0x71);
        }

        public void Dispose()
        {
            _transform.Dispose();
            _runtime.Dispose();
        }
    }
}
