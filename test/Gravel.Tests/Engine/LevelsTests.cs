using System.Linq;
using FluentAssertions;
using Xunit;

namespace Gravel.Engine;

public class LevelsTests
{
    static SstFile File(string name) => new($"mem://{name}", new DummyReader(), 0UL);

    [Fact]
    public void should_report_level_count_given_initialized_levels_when_querying()
    {
        // Arrange
        var lvls = new Levels(3);

        // Act
        var count = lvls.LevelCount;

        // Assert
        count.Should().Be(3);
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
        snap.Count.Should().Be(2);
        snap[0].Count.Should().Be(1);
        snap[1].Count.Should().Be(1);
        snap[0].First().Path.Should().Contain("a");
        snap[1].First().Path.Should().Contain("b");
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
        all.Select(f => f.Path).Should().HaveCount(3);
    }

    [Fact]
    public void should_indicate_true_given_threshold_met_when_meets_fan_in()
    {
        // Arrange
        var lvls = new Levels(1);
        lvls.Add(0, File("a"));
        lvls.Add(0, File("b"));

        // Act & Assert
        lvls.MeetsFanIn(0, 2).Should().BeTrue();
        lvls.MeetsFanIn(0, 3).Should().BeFalse();
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
        taken.Should().HaveCount(2);
        after[0].Should().BeEmpty();
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
        snap[0].Count.Should().Be(1); // immutable snapshot
        var newSnap = lvls.SnapshotLevels();
        newSnap[0].Count.Should().Be(2);
    }
}