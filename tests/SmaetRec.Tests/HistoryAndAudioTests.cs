using SmaetRec.Core;
using SmaetRec.Media;
using Xunit;

namespace SmaetRec.Tests;

public class HistoryTests
{
    private static Annotation Line(double x) => new() { Kind = AnnotationKind.Pen, Points = { new Vec2(x, 0), new Vec2(x + 10, 10) } };

    [Fact]
    public void Undo_and_redo_walk_the_same_steps_in_both_directions()
    {
        var store = new AnnotationStore();
        for (int i = 0; i < 5; i++) store.Add(Line(i * 20));
        store.Undo(); store.Undo();
        Assert.Equal(3, store.Count);
        store.Redo();
        Assert.Equal(4, store.Count);
        store.Add(Line(500));                 // a new drawing discards what could be redone
        Assert.False(store.CanRedo);
        Assert.Equal(5, store.Count);
        store.Clear();
        Assert.Equal(0, store.Count);
        store.Undo();                          // clearing is one step and can be taken back
        Assert.Equal(5, store.Count);
    }

    [Fact]
    public void History_is_bounded_and_keeps_the_newest_steps()
    {
        var store = new AnnotationStore();
        int total = AnnotationStore.MaxHistory + 50;
        for (int i = 0; i < total; i++) store.Add(Line(i));
        int undone = 0;
        while (store.CanUndo) { store.Undo(); undone++; }
        Assert.Equal(AnnotationStore.MaxHistory, undone);
        Assert.Equal(50, store.Count);         // older drawings stay; only their undo steps are gone
        while (store.CanRedo) store.Redo();
        Assert.Equal(total, store.Count);
    }

    [Fact]
    public void A_stored_drawing_does_not_change_when_the_caller_keeps_editing_its_object()
    {
        var store = new AnnotationStore();
        var a = Line(0);
        store.Add(a);
        a.Points.Add(new Vec2(999, 999));
        Assert.Equal(2, store.Snapshot()[0].Points.Count);
    }

    [Fact]
    public void Undo_during_a_stroke_keeps_the_stroke_visible_and_the_list_consistent()
    {
        var store = new AnnotationStore();
        store.Add(Line(0));
        store.SetInProgress(Line(50));
        store.Undo();
        Assert.Equal(0, store.Count);
        Assert.Single(store.Snapshot());       // only the stroke being drawn
        store.SetInProgress(null);
        Assert.Empty(store.Snapshot());
    }

    [Fact]
    public async Task Concurrent_drawing_undo_and_reading_do_not_corrupt_the_store()
    {
        var store = new AnnotationStore();
        var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(400));
        var reader = Task.Run(() => { while (!stop.IsCancellationRequested) foreach (var a in store.Snapshot()) _ = a.Points.Count; });
        var undoer = Task.Run(() => { while (!stop.IsCancellationRequested) { store.Undo(); store.Redo(); } });
        for (int i = 0; i < 2000; i++) store.Add(Line(i));
        stop.Cancel();
        await Task.WhenAll(reader, undoer);
        while (store.CanRedo) store.Redo();
        Assert.InRange(store.Count, 1, 2000);
    }
}

public class AudioBufferTests
{
    [Fact]
    public void A_reused_conversion_buffer_does_not_leak_old_samples_into_the_timeline()
    {
        var line = new AudioTimeline();
        var scratch = new float[2000];
        // First packet: 1000 loud frames. Second packet: 100 quiet frames into the same buffer.
        var loud = new byte[1000 * 2 * 2]; for (int i = 0; i < loud.Length; i += 2) BitConverter.TryWriteBytes(loud.AsSpan(i), (short)16000);
        var quiet = new byte[100 * 2 * 2]; for (int i = 0; i < quiet.Length; i += 2) BitConverter.TryWriteBytes(quiet.AsSpan(i), (short)1600);
        AudioMixer.ConvertToStereo(loud, 1000, 2, 2, false, scratch);
        line.Append(scratch.AsSpan(0, 2000), AudioTimeline.Rate, 1000);
        AudioMixer.ConvertToStereo(quiet, 100, 2, 2, false, scratch);
        line.Append(scratch.AsSpan(0, 200), AudioTimeline.Rate, 1100);
        Assert.Equal(1100, line.WritePosition);   // only the valid part of the buffer was taken
        var mix = new float[1100 * 2];
        line.MixInto(mix, 0, 1100);
        Assert.Equal(16000 / 32768f, mix[999 * 2], 4);
        Assert.Equal(1600 / 32768f, mix[1000 * 2], 4);
        Assert.Equal(1600 / 32768f, mix[1099 * 2 + 1], 4);
    }

    [Theory]
    [InlineData(1, 2, false)]
    [InlineData(2, 2, false)]
    [InlineData(2, 3, false)]
    [InlineData(2, 4, true)]
    [InlineData(6, 4, true)]
    public void Every_device_format_converts_to_stereo_at_the_right_level(int channels, int bytes, bool isFloat)
    {
        const int frames = 16;
        var buffer = new byte[frames * channels * bytes];
        for (int f = 0; f < frames; f++)
            for (int c = 0; c < channels; c++)
            {
                var at = buffer.AsSpan((f * channels + c) * bytes);
                if (isFloat) BitConverter.TryWriteBytes(at, 0.5f);
                else if (bytes == 2) BitConverter.TryWriteBytes(at, (short)16384);
                else { at[0] = 0; at[1] = 0; at[2] = 0x40; }
            }
        var stereo = AudioMixer.ConvertToStereo(buffer, frames, channels, bytes, isFloat, new float[frames * 2]);
        Assert.All(stereo, v => Assert.Equal(0.5f, v, 3));
    }

    [Fact]
    public void Silence_between_two_sounds_stays_silent()
    {
        var line = new AudioTimeline();
        var tone = Enumerable.Repeat(0.25f, 960).ToArray();
        line.Append(tone, AudioTimeline.Rate, 480);
        line.Append(tone, AudioTimeline.Rate, 48000);          // one second later
        var mix = new float[200];
        line.MixInto(mix, 20000, 100);
        Assert.All(mix, v => Assert.Equal(0f, v));
    }
}
