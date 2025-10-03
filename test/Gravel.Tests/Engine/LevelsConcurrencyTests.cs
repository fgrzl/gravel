using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace Gravel.Engine;

public class LevelsConcurrencyTests
{
    static SstFile File(string name)
    {
        return new SstFile($"mem://{name}", new DummyReader(), 0UL);
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
        snap.Count.Should().Be(3);
        snap.Select(l => l.Count).Sum().Should().Be(10);
        // Ensure snapshot immutability
        var first = snap[0].Count;
        var moreTasks = Enumerable.Range(0, 5).Select(i => Task.Run(() => lvls.Add(0, File($"g{i}"))));
        await Task.WhenAll(moreTasks);
        snap[0].Count.Should().Be(first);
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
        taken.Should().HaveCount(3);
        snap[0].Should().BeEmpty();
    }
}