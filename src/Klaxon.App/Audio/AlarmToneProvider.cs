using NAudio.Wave;

namespace Klaxon.App.Audio;

/// <summary>Synthesizes the built-in alarm tone: high beep, low beep, silence, repeat.</summary>
public sealed class AlarmToneProvider : ISampleProvider
{
    private const int SampleRate = 44_100;
    private const double Amplitude = 0.6;

    /// <summary>Fade-in/out length of each beep, to avoid clicks.</summary>
    private static readonly int RampSamples = SampleRate * 5 / 1000;

    /// <summary>Beep frequency in Hz (0 = silence) and duration in ms.</summary>
    private static readonly (double Hz, int Ms)[] Beeps =
    [
        (880, 150),
        (0, 80),
        (660, 150),
    ];

    private readonly (double Hz, int Samples)[] _segments;
    private readonly long _cycleSamples;
    private readonly bool _repeat;

    private long _position;
    private bool _finished;

    /// <param name="gap">Silence between one repeat and the next.</param>
    public AlarmToneProvider(TimeSpan gap, bool repeat)
    {
        _repeat = repeat;

        var segmentos = Beeps
            .Select(b => (b.Hz, Samples: (int)((long)b.Ms * SampleRate / 1000)))
            .ToList();

        if (repeat)
        {
            segmentos.Add((0d, (int)(gap.TotalSeconds * SampleRate)));
        }

        _segments = [.. segmentos];
        _cycleSamples = _segments.Sum(s => (long)s.Samples);

        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, channels: 1);
    }

    public WaveFormat WaveFormat { get; }

    /// <summary>Fills the buffer with the tone, looping while repeating.</summary>
    public int Read(float[] buffer, int offset, int count)
    {
        if (_finished)
        {
            return 0;
        }

        for (var i = 0; i < count; i++)
        {
            if (_position >= _cycleSamples)
            {
                if (!_repeat)
                {
                    _finished = true;
                    return i;
                }

                _position = 0;
            }

            buffer[offset + i] = SampleAt(_position++);
        }

        return count;
    }

    /// <summary>Computes one sample at the given position in the cycle.</summary>
    private float SampleAt(long position)
    {
        long inicio = 0;

        foreach (var (hz, samples) in _segments)
        {
            if (position < inicio + samples)
            {
                if (hz <= 0)
                {
                    return 0f;
                }

                var local = position - inicio;
                var onda = Math.Sin(2 * Math.PI * hz * local / SampleRate);

                return (float)(onda * Envelope(local, samples) * Amplitude);
            }

            inicio += samples;
        }

        return 0f;
    }

    /// <summary>Linear ramp at the beep edges, to avoid clicks.</summary>
    private static double Envelope(long position, int length)
    {
        var rampa = Math.Min(RampSamples, length / 4);
        if (rampa <= 0)
        {
            return 1d;
        }

        if (position < rampa)
        {
            return (double)position / rampa;
        }

        var restante = length - position;
        return restante < rampa ? (double)restante / rampa : 1d;
    }
}
