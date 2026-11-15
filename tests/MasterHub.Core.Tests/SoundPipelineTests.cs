using MasterHub.Core.Audio;
using NAudio.Wave;

namespace MasterHub.Core.Tests;

/// <summary>
/// T-14: the real decoding path (Media Foundation → stereo → resampler → volume → end of sound) on a tiny generated WAV.
/// No audio device is opened, so these run anywhere Media Foundation exists (Windows).
/// </summary>
public sealed class SoundPipelineTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mh_snd_" + Guid.NewGuid().ToString("N"));

    public SoundPipelineTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    /// <summary>A 16-bit sine of the given length, amplitude 0.5.</summary>
    private string Wav(string name, int sampleRate, int channels, double seconds)
    {
        string path = Path.Combine(_dir, name);
        using var writer = new WaveFileWriter(path, new WaveFormat(sampleRate, 16, channels));
        int frames = (int)(sampleRate * seconds);
        for (int i = 0; i < frames; i++)
        {
            short sample = (short)(Math.Sin(2 * Math.PI * 440 * i / sampleRate) * 0.5 * short.MaxValue);
            for (int c = 0; c < channels; c++) writer.WriteSample(sample / 32768f);
        }
        return path;
    }

    private static float[] ReadAll(MixerVoice voice)
    {
        var all = new List<float>();
        var buffer = new float[4096];
        int n;
        while ((n = voice.Read(buffer)) > 0) all.AddRange(buffer.Take(n));
        return all.ToArray();
    }

    [Fact]
    public void AMonoFileAtAnotherRate_BecomesStereoAtTheOutputRate()
    {
        string file = Wav("mono.wav", 22050, 1, 0.5);

        var voice = SoundPipeline.Open(file, 48000, gain: 1f);
        var samples = ReadAll(voice);

        Assert.Equal(2, voice.WaveFormat.Channels);
        Assert.Equal(48000, voice.WaveFormat.SampleRate);
        // 0.5 s at 48 kHz, two channels (the resampler may be a few frames off at the edges)
        Assert.InRange(samples.Length, 48000 - 400, 48000 + 400);
        Assert.InRange(samples.Max(), 0.4f, 0.6f);
    }

    [Fact]
    public void Volume_ScalesTheSamples_AndCanLiftAboveTheFile()
    {
        string file = Wav("tone.wav", 48000, 2, 0.2);

        float normal = ReadAll(SoundPipeline.Open(file, 48000, 1f)).Max();
        float quiet = ReadAll(SoundPipeline.Open(file, 48000, 0.5f)).Max();
        float loud = ReadAll(SoundPipeline.Open(file, 48000, 1.5f)).Max();
        float muted = ReadAll(SoundPipeline.Open(file, 48000, 0f)).Max();

        Assert.Equal(normal * 0.5f, quiet, 2);
        Assert.Equal(normal * 1.5f, loud, 2);
        Assert.Equal(0f, muted);
    }

    [Fact]
    public void AVoice_ReportsItsEndOnce_AndThenStopsPlaying()
    {
        string file = Wav("short.wav", 48000, 2, 0.1);
        var voice = SoundPipeline.Open(file, 48000, 1f);
        int ended = 0;
        using var done = new ManualResetEventSlim();
        voice.Finished += () => { Interlocked.Increment(ref ended); done.Set(); };

        Assert.True(voice.IsPlaying);
        ReadAll(voice);

        Assert.True(done.Wait(TimeSpan.FromSeconds(5)));
        Assert.False(voice.IsPlaying);
        voice.Read(new float[64]);                                  // reading a finished voice again must not report twice
        Thread.Sleep(100);
        Assert.Equal(1, ended);
    }

    [Fact]
    public void Stop_MakesTheNextReadEndTheVoice()
    {
        string file = Wav("long.wav", 48000, 2, 2.0);
        var voice = SoundPipeline.Open(file, 48000, 1f);
        using var done = new ManualResetEventSlim();
        voice.Finished += done.Set;

        Assert.Equal(512, voice.Read(new float[512]));              // plays
        voice.Stop();

        Assert.False(voice.IsPlaying);                              // at once, before the audio thread notices
        Assert.Equal(0, voice.Read(new float[512]));                // the audio thread's next read ends it
        Assert.True(done.Wait(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void AFileThatIsNotAudio_IsAReason_NotACrash()
    {
        string file = Path.Combine(_dir, "broken.wav");
        File.WriteAllText(file, "this is not a wave file");

        var ex = Assert.Throws<SoundException>(() => SoundPipeline.Open(file, 48000, 1f));

        Assert.Equal(SoundProblem.PlaybackFailed, ex.Problem);
    }

    [Fact]
    public void AFileWithMoreThanTwoChannels_IsRefusedWithAClearReason()
    {
        string file = Wav("surround.wav", 48000, 4, 0.1);

        var ex = Assert.Throws<SoundException>(() => SoundPipeline.Open(file, 48000, 1f));

        Assert.Equal(SoundProblem.UnsupportedType, ex.Problem);
    }
}
