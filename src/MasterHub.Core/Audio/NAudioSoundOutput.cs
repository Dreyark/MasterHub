using System;
using System.Runtime.InteropServices;
using System.Threading;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace MasterHub.Core.Audio;

/// <summary>Opens WASAPI outputs (shared mode) for the sound board. Windows only; nothing is opened until a sound is played.</summary>
public sealed class NAudioSoundOutputFactory : ISoundOutputFactory
{
    public ISoundDevice Open(string deviceId) => new NAudioSoundDevice(deviceId);
}

/// <summary>
/// One WASAPI output with a mixer in front of it: every sound is a voice that is mixed in, so overlapping sounds need no
/// second output stream. Files are decoded by Media Foundation (WAV, MP3, AAC, WMA, FLAC).
/// </summary>
internal sealed class NAudioSoundDevice : ISoundDevice
{
    private readonly MMDeviceEnumerator _enumerator = new();
    private readonly MMDevice _device;
    private readonly WasapiPlayer _output;
    private readonly MixingSampleProvider _mixer;
    private readonly int _sampleRate;

    public NAudioSoundDevice(string deviceId)
    {
        try
        {
            _device = string.IsNullOrEmpty(deviceId)
                ? _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia)
                : _enumerator.GetDevice(deviceId);

            if (_device.State != DeviceState.Active) throw new SoundException(SoundProblem.DeviceMissing, "The output device is not active.");

            using (var client = _device.CreateAudioClient()) _sampleRate = client.MixFormat.SampleRate;

            // the mixer speaks the sample rate the device's shared-mode engine already uses, so no conversion is left to the driver
            _mixer = new MixingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(_sampleRate, 2)) { ReadFully = true };
            _output = new WasapiPlayerBuilder().WithDevice(_device).WithSharedMode().WithEventSync().WithLatency(100).Build();
            _output.Init(_mixer.ToWaveProvider());
            _output.Play();
        }
        catch (SoundException)
        {
            throw;
        }
        catch (COMException ex)
        {
            throw new SoundException(SoundProblem.DeviceMissing, "The output device could not be opened.", ex);
        }
        catch (Exception ex)
        {
            throw new SoundException(SoundProblem.PlaybackFailed, ex.Message, ex);
        }
    }

    public ISoundVoice Start(string file, float gain)
    {
        var voice = SoundPipeline.Open(file, _sampleRate, gain);
        _mixer.AddMixerInput(voice);
        return voice;
    }

    public void Dispose()
    {
        try { _output.Stop(); } catch { }
        try { _output.Dispose(); } catch { }
        try { _device.Dispose(); } catch { }
        try { _enumerator.Dispose(); } catch { }
    }
}

/// <summary>Opens a file the way the sound board plays it: decoded, stereo, at the output's sample rate, with the key's volume.</summary>
internal static class SoundPipeline
{
    /// <summary>Throws <see cref="SoundException"/> when the file cannot be decoded or has more than two channels.</summary>
    public static MixerVoice Open(string file, int sampleRate, float gain)
    {
        MediaFoundationReader? reader = null;
        try
        {
            reader = new MediaFoundationReader(file);
            ISampleProvider source = reader.ToSampleProvider();

            if (source.WaveFormat.Channels == 1) source = new MonoToStereoSampleProvider(source);
            else if (source.WaveFormat.Channels != 2) throw new SoundException(SoundProblem.UnsupportedType, "Only mono and stereo files are supported.");

            if (source.WaveFormat.SampleRate != sampleRate) source = new WdlResamplingSampleProvider(source, sampleRate);

            return new MixerVoice(new VolumeSampleProvider(source) { Volume = gain }, reader);
        }
        catch (SoundException)
        {
            reader?.Dispose();
            throw;
        }
        catch (Exception ex)
        {
            reader?.Dispose();
            throw new SoundException(SoundProblem.PlaybackFailed, ex.Message, ex);
        }
    }
}

/// <summary>
/// A sound in the mixer. <see cref="Stop"/> only raises a flag: the audio thread sees it on its next read, returns
/// "nothing more" (the mixer then drops the voice) and releases the decoder, so the decoder is never closed under a read.
/// </summary>
internal sealed class MixerVoice : ISampleProvider, ISoundVoice
{
    private readonly ISampleProvider _source;
    private readonly IDisposable _reader;
    private int _ended;               // 1 once the end was reported
    private volatile bool _stopRequested;

    public MixerVoice(ISampleProvider source, IDisposable reader)
    {
        _source = source;
        _reader = reader;
    }

    public WaveFormat WaveFormat => _source.WaveFormat;

    public bool IsPlaying => Volatile.Read(ref _ended) == 0 && !_stopRequested;

    public event Action? Finished;

    public void Stop() => _stopRequested = true;

    public int Read(Span<float> buffer)
    {
        if (_stopRequested || Volatile.Read(ref _ended) != 0)
        {
            End();
            return 0;
        }

        int read = _source.Read(buffer);
        if (read == 0) End();
        return read;
    }

    private void End()
    {
        if (Interlocked.Exchange(ref _ended, 1) != 0) return;

        try { _reader.Dispose(); } catch { }
        // reported from the thread pool, never from the audio thread, so a listener can take its time (and its locks)
        ThreadPool.QueueUserWorkItem(_ => Finished?.Invoke());
    }
}
