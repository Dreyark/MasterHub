using MasterHub.Core.Devices;

namespace MasterHub.Core.Tests;

/// <summary>T-30: a key picture that did not change is not sent again, and a changed one never is skipped.</summary>
public class SentKeyImagesTests
{
    private static byte[] Jpeg(params byte[] data) => data;

    [Fact]
    public void ThePictureThatIsAlreadyShown_IsSkipped_AndCounted()
    {
        var sent = new SentKeyImages();
        Assert.False(sent.IsAlreadyShown(3, Jpeg(1, 2, 3), 0));   // nothing known yet
        sent.Remember(3, Jpeg(1, 2, 3), 0);

        Assert.True(sent.IsAlreadyShown(3, Jpeg(1, 2, 3), 0));    // an equal copy, not the same array
        Assert.Equal(1, sent.Sent);
        Assert.Equal(1, sent.Skipped);
    }

    [Fact]
    public void AChangedPicture_IsNeverSkipped_EvenByOneByte()
    {
        var sent = new SentKeyImages();
        sent.Remember(0, Jpeg(1, 2, 3, 4), 0);

        Assert.False(sent.IsAlreadyShown(0, Jpeg(1, 2, 3, 5), 0));
        Assert.False(sent.IsAlreadyShown(0, Jpeg(1, 2, 3), 0));      // shorter
        Assert.False(sent.IsAlreadyShown(0, Jpeg(1, 2, 3, 4, 0), 0)); // longer
        Assert.Equal(0, sent.Skipped);
    }

    [Fact]
    public void TheKeysAreIndependent()
    {
        var sent = new SentKeyImages();
        sent.Remember(0, Jpeg(9), 0);

        Assert.False(sent.IsAlreadyShown(1, Jpeg(9), 0));
    }

    [Fact]
    public void ADifferentRotation_MeansTheKeyShowsSomethingElse()
    {
        var sent = new SentKeyImages();
        sent.Remember(0, Jpeg(9), 0);

        Assert.False(sent.IsAlreadyShown(0, Jpeg(9), 180));
    }

    [Fact]
    public void Forget_MakesTheNextPictureGoOut_OnlyForThatKey()
    {
        var sent = new SentKeyImages();
        sent.Remember(0, Jpeg(1), 0);
        sent.Remember(1, Jpeg(1), 0);

        sent.Forget(0);

        Assert.False(sent.IsAlreadyShown(0, Jpeg(1), 0));
        Assert.True(sent.IsAlreadyShown(1, Jpeg(1), 0));
    }

    [Fact]
    public void Clear_ForgetsEverything()
    {
        var sent = new SentKeyImages();
        for (int i = 0; i < 15; i++) sent.Remember(i, Jpeg((byte)i), 0);

        sent.Clear();   // a reconnect, a standby, a whole-panel picture

        for (int i = 0; i < 15; i++) Assert.False(sent.IsAlreadyShown(i, Jpeg((byte)i), 0));
    }

    [Fact]
    public void ABufferTheCallerReuses_DoesNotFoolTheComparison()
    {
        var sent = new SentKeyImages();
        var buffer = Jpeg(1, 2, 3);
        sent.Remember(0, buffer, 0);

        buffer[0] = 99;   // the caller renders the next picture into the same array

        Assert.False(sent.IsAlreadyShown(0, buffer, 0));
        Assert.True(sent.IsAlreadyShown(0, Jpeg(1, 2, 3), 0));
    }

    [Fact]
    public void AnIndexOutsideTheKeypad_IsNeverSkippedAndNeverThrows()
    {
        var sent = new SentKeyImages();

        sent.Remember(15, Jpeg(1), 0);
        sent.Remember(-1, Jpeg(1), 0);
        sent.Forget(99);

        Assert.False(sent.IsAlreadyShown(15, Jpeg(1), 0));
        Assert.False(sent.IsAlreadyShown(-1, Jpeg(1), 0));
        Assert.Equal(0, sent.Sent);
    }

    [Fact]
    public async Task ItSurvivesManyThreads()
    {
        var sent = new SentKeyImages();

        await Task.WhenAll(Enumerable.Range(0, 8).Select(t => Task.Run(() =>
        {
            for (int i = 0; i < 2000; i++)
            {
                int key = i % 15;
                if (!sent.IsAlreadyShown(key, Jpeg((byte)(i % 3)), 0)) sent.Remember(key, Jpeg((byte)(i % 3)), 0);
                if (i % 50 == 0) sent.Clear();
            }
        })));

        Assert.True(sent.Sent > 0);
        Assert.True(sent.Skipped > 0);
    }
}
