using snipv.Services;
using Xunit.Abstractions;

namespace snipv.Tests;

/// <summary>
/// The probe answers with whatever keyboard layouts this machine happens to
/// have, so these tests check the shape of that answer rather than its content,
/// and print it: the printed line is how a wrong detection gets noticed.
/// </summary>
public class KeyboardLayoutProbeTests(ITestOutputHelper output)
{
    [Fact]
    public void DetectReportsKeysThisMachineTypesWithAltGr()
    {
        var keys = KeyboardLayoutProbe.Detect();

        output.WriteLine($"detected on this machine -> {keys}");

        // Only keys a shortcut can use may come back, and Windows must not have
        // claimed every one of them.
        var candidates = Enumerable.Range('0', 10).Concat(Enumerable.Range('A', 26)).Select(c => (uint)c).ToArray();
        var taken = candidates.Where(k => keys.Types(k, withShift: false) || keys.Types(k, withShift: true)).ToArray();
        var untouched = candidates.Except(taken).ToArray();

        Assert.NotEmpty(untouched);
        Assert.All(taken, key => Assert.Contains(key, candidates));
    }

    [Fact]
    public void DetectIsStableAcrossCalls()
    {
        // A dead key left pending inside a layout would make the second sweep
        // answer differently from the first.
        var first = KeyboardLayoutProbe.Detect();
        var second = KeyboardLayoutProbe.Detect();

        Assert.Equal(first.ToString(), second.ToString());
    }

    [Fact]
    public void FunctionKeysAreNeverReported()
    {
        var keys = KeyboardLayoutProbe.Detect();

        for (uint vk = 0x70; vk <= 0x7B; vk++) // F1-F12
        {
            Assert.False(keys.Types(vk, withShift: false));
            Assert.False(keys.Types(vk, withShift: true));
        }
    }
}
