using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Gravel.TestHelpers;
using Xunit;

namespace Gravel.Engine;

public class LevelsTests
{
    static SstFile File(string name)
    {
        return new SstFile($"mem://{name}", new DummyReader(), 0UL);
    }

    [Fact]
    public void should_report_level_count_given_initialized_levels_when_querying()
    {
        // Arrange
        var lvls = new Levels(3);

        // Act
        var count = lvls.LevelCount;

        // Assert
        Assert.Equal(3, count);
    }

    [Fact]
    public void should_add_files_and_return_in_snapshot_given_adds_when_snapshot_levels()
    {
        // Arrange
        var lvls = new Levels(2);
        lvls.Add(0, File("a"));
        lvls.Add(1, File("b"));

        // Act
        var snap = lvls.SnapshotLevels();

        // Assert
        Assert.Equal(2, snap.Count);
        Assert.Equal(1, snap[0].Count);
        Assert.Equal(1, snap[1].Count);
        Assert.Contains("a", snap[0].First().Path);
        Assert.Contains("b", snap[1].First().Path);
    }

    [Fact]
    public void should_return_all_files_in_snapshot_all_given_files_added_when_snapshot_all()
    {
        // Arrange
        var lvls = new Levels(2);
        lvls.Add(0, File("a"));
        lvls.Add(0, File("b"));
        lvls.Add(1, File("c"));

        // Act
        var all = lvls.SnapshotAll();

        // Assert
        Assert.Equal(3, all.Select(f => f.Path).Count());
    }

    [Fact]
    public void should_indicate_true_given_threshold_met_when_meets_fan_in()
    {
        // Arrange
        var lvls = new Levels(1);
        lvls.Add(0, File("a"));
        lvls.Add(0, File("b"));

        // Act & Assert
        Assert.True(lvls.MeetsFanIn(0, 2));
        Assert.False(lvls.MeetsFanIn(0, 3));
    }

    [Fact]
    public void should_take_level_and_clear_original_given_files_when_take_level()
    {
        // Arrange
        var lvls = new Levels(1);
        lvls.Add(0, File("a"));
        lvls.Add(0, File("b"));

        // Act
        var taken = lvls.TakeLevel(0);
        var after = lvls.SnapshotLevels();

        // Assert
        Assert.Equal(2, taken.Count);
        Assert.Empty(after[0]);
    }

    [Fact]
    public void should_not_reflect_mutations_after_snapshot_given_mutation_after_snapshot_when_snapshot_levels()
    {
        // Arrange
        var lvls = new Levels(1);
        lvls.Add(0, File("a"));
        var snap = lvls.SnapshotLevels();

        // Act
        lvls.Add(0, File("b"));

        // Assert
        Assert.Equal(1, snap[0].Count); // immutable snapshot
        var newSnap = lvls.SnapshotLevels();
        Assert.Equal(2, newSnap[0].Count);
    }

    [Fact]
    public async Task should_be_thread_safe_given_concurrent_adds_when_snapshot_levels()
    {
        // Arrange
        var lvls = new Levels(3);
        var tasks = new List<Task>();

        // Act: concurrently add files to each level
        for (var i = 0; i < 10; i++)
        {
            var idx = i;
            tasks.Add(Task.Run(() => lvls.Add(idx % 3, File($"f{idx}"))));
        }

        await Task.WhenAll(tasks);

        // Assert: snapshot is consistent and counts match
        var snap = lvls.SnapshotLevels();
        Assert.Equal(3, snap.Count);
        Assert.Equal(10, snap.Select(l => l.Count).Sum());
        // Ensure snapshot immutability
        var first = snap[0].Count;
        var moreTasks = Enumerable.Range(0, 5).Select(i => Task.Run(() => lvls.Add(0, File($"g{i}"))));
        await Task.WhenAll(moreTasks);
        Assert.Equal(first, snap[0].Count);
    }

    [Fact]
    public void should_return_isolated_list_given_take_level_when_mutated_after()
    {
        // Arrange
        var lvls = new Levels(1);
        lvls.Add(0, File("a"));
        lvls.Add(0, File("b"));

        // Act
        var taken = lvls.TakeLevel(0);
        taken.Add(File("c")); // mutate returned list
        var snap = lvls.SnapshotLevels();

        // Assert
        Assert.Equal(3, taken.Count);
        Assert.Empty(snap[0]);
    }
}
