using System.IO;
using Klaxon.Core.Model;
using Microsoft.Extensions.Logging;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Klaxon.App.Audio;

/// <summary>Plays an alert's sound; one instance per on-screen alert.</summary>
public sealed class AlarmAudioPlayer(ILogger log) : IDisposable
{
    private readonly ILogger _log = log;

    private WaveOutEvent? _output;
    private AudioFileReader? _reader;
    private bool _disposed;

    /// <summary>Plays the given sound (volume, fade and loop applied).</summary>
    public void Play(SoundSpec spec)
    {
        if (_disposed || spec.IsSilent)
        {
            return;
        }

        try
        {
            Stop();

            ISampleProvider source = BuildSource(spec);

            if (spec.Volume < 1d)
            {
                source = new VolumeSampleProvider(source) { Volume = (float)Math.Clamp(spec.Volume, 0d, 1d) };
            }

            if (spec.FadeIn > TimeSpan.Zero)
            {
                var fade = new FadeInOutSampleProvider(source, initiallySilent: true);
                fade.BeginFadeIn(spec.FadeIn.TotalMilliseconds);
                source = fade;
            }

            _output = new WaveOutEvent();
            _output.Init(source);
            _output.Play();
        }
        catch (Exception ex)
        {
            // Keep the visual alert even if audio fails.
            _log.LogError(ex, "Falha ao tocar o som do alerta. Seguindo em silêncio.");
            Stop();
        }
    }

    /// <summary>Builds the sample source: the user's file, or the built-in tone.</summary>
    private ISampleProvider BuildSource(SoundSpec spec)
    {
        if (spec.FilePath is { Length: > 0 } path && File.Exists(path))
        {
            _reader = new AudioFileReader(path);
            return spec.Loop ? new LoopingSampleProvider(_reader) : _reader;
        }

        if (spec.FilePath is { Length: > 0 } missing)
        {
            _log.LogWarning("Som {Caminho} não encontrado. Usando o toque padrão.", missing);
        }

        var loop = spec.Loop || spec.RepeatEvery > TimeSpan.Zero;
        var interval = spec.RepeatEvery > TimeSpan.Zero ? spec.RepeatEvery : TimeSpan.FromMilliseconds(700);

        return new AlarmToneProvider(interval, loop);
    }

    /// <summary>Stops playback and releases audio resources.</summary>
    public void Stop()
    {
        _output?.Dispose();
        _output = null;

        _reader?.Dispose();
        _reader = null;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();
    }
}
