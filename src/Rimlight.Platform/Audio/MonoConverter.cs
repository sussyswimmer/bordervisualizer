using System.Runtime.InteropServices;

namespace Rimlight.Platform.Audio;

internal enum SampleEncoding
{
    Float32,
    Int16,
    Int24,
    Int32,
}

// The shared-mode mix format of the captured endpoint, reduced to what conversion needs (doc 03 §1: usually 32-bit
// float at 48 kHz, stereo or more; 16/24/32-bit integer is handled too).
internal readonly record struct CaptureFormat(SampleEncoding Encoding, int Channels, int SampleRate)
{
    public int BytesPerSample => Encoding switch
    {
        SampleEncoding.Int16 => 2,
        SampleEncoding.Int24 => 3,
        _ => 4,
    };

    public int BytesPerFrame => BytesPerSample * Channels;

    public override string ToString() => Encoding switch
    {
        SampleEncoding.Float32 => $"{SampleRate} Hz, {Channels} ch, 32-bit float",
        _ => $"{SampleRate} Hz, {Channels} ch, {BytesPerSample * 8}-bit PCM",
    };
}

// Interleaved little-endian frames to mono floats by averaging the channels (doc 03 §1). Runs on the audio thread:
// no allocation, no locks, no logging.
internal static class MonoConverter
{
    // Converts whole frames from data into destination and returns the number of frames written, which is limited by
    // both. A trailing partial frame is ignored.
    public static int Convert(ReadOnlySpan<byte> data, CaptureFormat format, Span<float> destination)
    {
        int channels = format.Channels;
        int frames = Math.Min(data.Length / format.BytesPerFrame, destination.Length);
        if (frames <= 0 || channels <= 0) return 0;
        data = data[..(frames * format.BytesPerFrame)];
        float average = 1f / channels;

        switch (format.Encoding)
        {
            case SampleEncoding.Float32:
            {
                ReadOnlySpan<float> samples = MemoryMarshal.Cast<byte, float>(data);
                for (int frame = 0, s = 0; frame < frames; frame++)
                {
                    float sum = 0;
                    for (int c = 0; c < channels; c++) sum += samples[s++];
                    destination[frame] = sum * average;
                }
                break;
            }
            case SampleEncoding.Int16:
            {
                ReadOnlySpan<short> samples = MemoryMarshal.Cast<byte, short>(data);
                float scale = average / 32768f;
                for (int frame = 0, s = 0; frame < frames; frame++)
                {
                    int sum = 0;
                    for (int c = 0; c < channels; c++) sum += samples[s++];
                    destination[frame] = sum * scale;
                }
                break;
            }
            case SampleEncoding.Int24:
            {
                float scale = average / 8388608f;
                for (int frame = 0, b = 0; frame < frames; frame++)
                {
                    int sum = 0;
                    for (int c = 0; c < channels; c++, b += 3)
                        sum += data[b] | (data[b + 1] << 8) | ((sbyte)data[b + 2] << 16);
                    destination[frame] = sum * scale;
                }
                break;
            }
            case SampleEncoding.Int32:
            {
                ReadOnlySpan<int> samples = MemoryMarshal.Cast<byte, int>(data);
                double scale = average / 2147483648.0;
                for (int frame = 0, s = 0; frame < frames; frame++)
                {
                    long sum = 0;
                    for (int c = 0; c < channels; c++) sum += samples[s++];
                    destination[frame] = (float)(sum * scale);
                }
                break;
            }
            default:
                return 0;
        }
        return frames;
    }
}
