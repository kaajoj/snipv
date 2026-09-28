using snipv.Services;

namespace snipv.Tests;

public class SnippetServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "snipv-tests-" + Guid.NewGuid());
    private readonly string _file;

    public SnippetServiceTests()
    {
        Directory.CreateDirectory(_dir);
        _file = Path.Combine(_dir, "snippets.json");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    [Fact]
    public void StartsEmptyWhenFileDoesNotExist()
    {
        var service = new SnippetService(_file);

        Assert.Empty(service.GetSnippets());
    }

    [Fact]
    public void AddedSnippetSurvivesReload()
    {
        new SnippetService(_file).AddSnippet("Ctrl+Alt+1", "hello");

        var reloaded = new SnippetService(_file);
        var snippet = Assert.Single(reloaded.GetSnippets());
        Assert.Equal("Ctrl+Alt+1", snippet.Shortcut);
        Assert.Equal("hello", snippet.Content);
    }

    [Fact]
    public void AddingOverATakenShortcutParksThePreviousHolder()
    {
        var service = new SnippetService(_file);
        service.AddSnippet("Ctrl+Alt+T", "first");

        service.AddSnippet("ctrl+alt+t", "second");

        var snippets = service.GetSnippets();
        Assert.Equal(2, snippets.Count);
        Assert.Equal("second", snippets[0].Content);
        Assert.Equal("Ctrl+Alt+T", snippets[0].Shortcut, ignoreCase: true);
        Assert.Equal("first", snippets[1].Content);
        Assert.False(snippets[1].HasShortcut);
    }

    [Fact]
    public void UpdatingOntoATakenShortcutParksTheOtherSnippet()
    {
        var service = new SnippetService(_file);
        service.AddSnippet("Ctrl+Alt+1", "one");
        service.AddSnippet("Ctrl+Alt+2", "two");

        var two = service.GetSnippets().Single(s => s.Content == "two");
        service.UpdateSnippet(two, "Ctrl+Alt+1", "two");

        var snippets = service.GetSnippets();
        Assert.Equal(2, snippets.Count);
        Assert.Equal("two", Assert.Single(snippets, s => s.Shortcut == "Ctrl+Alt+1").Content);
        Assert.Equal("one", Assert.Single(snippets, s => s.HasNoShortcut).Content);
    }

    [Fact]
    public void ParkedSnippetSurvivesReloadWithoutAShortcut()
    {
        var service = new SnippetService(_file);
        service.AddSnippet("Ctrl+Alt+1", "parked");
        service.AddSnippet("Ctrl+Alt+1", "took over");

        var reloaded = new SnippetService(_file).GetSnippets();

        Assert.Equal(2, reloaded.Count);
        Assert.Equal("parked", Assert.Single(reloaded, s => s.HasNoShortcut).Content);
    }

    [Fact]
    public void SnippetCanBeAddedWithoutAShortcut()
    {
        var service = new SnippetService(_file);

        service.AddSnippet(null, "no shortcut yet");
        service.AddSnippet("   ", "blank counts as none");

        var snippets = new SnippetService(_file).GetSnippets();
        Assert.Equal(2, snippets.Count);
        Assert.All(snippets, s => Assert.False(s.HasShortcut));
    }

    [Fact]
    public void ParkedSnippetsDoNotCollideWithEachOther()
    {
        var service = new SnippetService(_file);
        service.AddSnippet(null, "same text");
        service.AddSnippet(null, "same text");

        Assert.Equal(2, service.GetSnippets().Count);
    }

    [Fact]
    public void UpdateReassignsShortcutWithoutLeavingTheOldOne()
    {
        var service = new SnippetService(_file);
        service.AddSnippet("Ctrl+Alt+1", "hello");

        service.UpdateSnippet(service.GetSnippets().Single(), "Ctrl+Alt+2", "hello v2");

        var snippet = Assert.Single(service.GetSnippets());
        Assert.Equal("Ctrl+Alt+2", snippet.Shortcut);
        Assert.Equal("hello v2", snippet.Content);
        Assert.False(service.Contains("Ctrl+Alt+1"));
    }

    [Fact]
    public void UpdateIgnoresASnippetThatIsNoLongerStored()
    {
        var service = new SnippetService(_file);
        service.AddSnippet("Ctrl+Alt+1", "hello");
        var stale = service.GetSnippets().Single();
        service.RemoveSnippet(stale);

        service.UpdateSnippet(stale, "Ctrl+Alt+2", "resurrected");

        Assert.Empty(service.GetSnippets());
    }

    [Fact]
    public void RemoveDeletesAndPersists()
    {
        var service = new SnippetService(_file);
        service.AddSnippet("Ctrl+Alt+1", "hello");

        service.RemoveSnippet(service.GetSnippets().Single());

        Assert.Empty(service.GetSnippets());
        Assert.Empty(new SnippetService(_file).GetSnippets());
    }

    [Fact]
    public void EventsFireOnAddUpdateRemoveButNotOnMissingRemove()
    {
        var service = new SnippetService(_file);
        var raised = 0;
        service.SnippetsChanged += () => raised++;

        service.AddSnippet("Ctrl+Alt+1", "a");
        var added = service.GetSnippets().Single();
        service.UpdateSnippet(added, "Ctrl+Alt+2", "b");
        var updated = service.GetSnippets().Single();
        service.RemoveSnippet(updated);
        Assert.Equal(3, raised);

        service.RemoveSnippet(updated); // already gone
        Assert.Equal(3, raised);
    }

    [Fact]
    public void SaveLeavesNoTemporaryFileBehind()
    {
        var service = new SnippetService(_file);
        service.AddSnippet("Ctrl+Alt+1", "hello");
        service.AddSnippet("Ctrl+Alt+2", "again");

        Assert.Equal([Path.GetFileName(_file)], Directory.GetFiles(_dir).Select(Path.GetFileName));
    }

    [Fact]
    public void SaveReplacesTheFileWholeRatherThanTruncatingIt()
    {
        var service = new SnippetService(_file);
        service.AddSnippet("Ctrl+Alt+1", "the long original entry that must not be left half written");

        // A leftover temp file from an earlier crash must not block the next save.
        File.WriteAllText(_file + ".tmp", "stale");
        service.AddSnippet("Ctrl+Alt+2", "second");

        Assert.Equal(2, new SnippetService(_file).GetSnippets().Count);
        Assert.False(File.Exists(_file + ".tmp"));
    }

    [Fact]
    public void AFailedSaveKeepsTheOldFileAndLeavesNoTemporaryOne()
    {
        var service = new SnippetService(_file);
        service.AddSnippet("Ctrl+Alt+1", "first");
        File.SetAttributes(_file, FileAttributes.ReadOnly);

        try
        {
            // The new list cannot be moved over a read-only file.
            service.AddSnippet("Ctrl+Alt+2", "second");

            Assert.False(File.Exists(_file + ".tmp"));
        }
        finally
        {
            File.SetAttributes(_file, FileAttributes.Normal);
        }

        var onDisk = Assert.Single(new SnippetService(_file).GetSnippets());
        Assert.Equal("first", onDisk.Content);
    }

    [Fact]
    public void SaveFailureIsReportedOnceUntilSavingWorksAgain()
    {
        var folder = Path.Combine(_dir, "gone");
        var file = Path.Combine(folder, "snippets.json");
        var service = new SnippetService(file);
        List<string> failures = [];
        service.SaveFailed += failures.Add;

        // The folder does not exist, so nothing can be written.
        service.AddSnippet("Ctrl+Alt+1", "a");
        service.AddSnippet("Ctrl+Alt+2", "b");
        Assert.Single(failures);
        Assert.NotEmpty(failures[0]);

        // The change is kept in memory even though the file could not take it.
        Assert.Equal(2, service.GetSnippets().Count);

        Directory.CreateDirectory(folder);
        service.AddSnippet("Ctrl+Alt+3", "c");
        Assert.Single(failures); // a save that works says nothing

        Directory.Delete(folder, recursive: true);
        service.AddSnippet("Ctrl+Alt+4", "d");
        Assert.Equal(2, failures.Count); // broken again, so said again
    }

    [Fact]
    public void CorruptFileLoadsAsEmptyInsteadOfThrowing()
    {
        File.WriteAllText(_file, "{ this is not json");

        var service = new SnippetService(_file);

        Assert.Empty(service.GetSnippets());
    }

    [Fact]
    public void CorruptFileIsSetAsideInsteadOfBeingOverwritten()
    {
        File.WriteAllText(_file, "{ this is not json");

        new SnippetService(_file).AddSnippet("Ctrl+Alt+1", "fresh start");

        Assert.Equal("{ this is not json", File.ReadAllText(_file + ".corrupt"));
    }

    [Fact]
    public void GetSnippetsSortsByShortcutAndPutsParkedOnesLast()
    {
        var service = new SnippetService(_file);
        service.AddSnippet("Ctrl+Alt+9", "nine");
        service.AddSnippet(null, "parked");
        service.AddSnippet("Ctrl+Alt+1", "one");
        service.AddSnippet("Ctrl+Alt+5", "five");

        var contents = service.GetSnippets().Select(s => s.Content).ToArray();

        Assert.Equal(["one", "five", "nine", "parked"], contents);
    }

    [Fact]
    public void ContainsIgnoresCase()
    {
        var service = new SnippetService(_file);
        service.AddSnippet("Ctrl+Alt+T", "x");

        Assert.True(service.Contains("CTRL+ALT+T"));
        Assert.False(service.Contains("Ctrl+Alt+U"));
    }

    [Fact]
    public void FindByShortcutReturnsTheHolderOrNull()
    {
        var service = new SnippetService(_file);
        service.AddSnippet("Ctrl+Alt+T", "x");
        service.AddSnippet(null, "parked");

        Assert.Equal("x", service.FindByShortcut("ctrl+alt+t")?.Content);
        Assert.Null(service.FindByShortcut("Ctrl+Alt+U"));
        Assert.Null(service.FindByShortcut(""));
    }
}
