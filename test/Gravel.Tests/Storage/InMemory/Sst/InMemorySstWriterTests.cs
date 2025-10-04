using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Gravel.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Gravel.Storage.InMemory.Sst;

public class InMemorySstWriterTests
{
    static InMemorySstFactory CreateFactory(bool dedupe = true)
    {
        return new InMemorySstFactory(Options.Create(new InMemorySstOptions { DeduplicateOnSeal = dedupe }));
    }

    static DbEntry E(string k, string v, ulong seq)
    {
        return DbEntry.Put(Encoding.UTF8.GetBytes(k), Encoding.UTF8.GetBytes(v), seq);
    }

    [Fact]
    public async Task should_publish_snapshot_given_dispose_when_writer_completes()
    {
        // Arrange
        var f = CreateFactory();

        // Act
        await using (var w = f.CreateWriter("/t1", 2))
        {
            static async IAsyncEnumerable<DbEntry> Data()
            {
                await Task.Yield();
                yield return E("a", "1", 1);
                yield return E("b", "2", 2);
            }

            await w.WriteAsync(Data());
        }

        // Assert
        f.ListPaths().Should().Contain("/t1");
    }

    [Fact]
    public async Task should_deduplicate_to_last_value_given_duplicate_keys_when_dedupe_enabled()
    {
        // Arrange
        var f = CreateFactory();

        // Act
        await using (var w = f.CreateWriter("/dedupe", 2))
        {
            static async IAsyncEnumerable<DbEntry> Data()
            {
                await Task.Yield();
                yield return E("k", "v1", 1);
                yield return E("k", "v2", 2);
            }

            await w.WriteAsync(Data());
        }

        // Assert
        var r = f.CreateReader("/dedupe");
        var e = await r.GetAsync("k"u8.ToArray());
        Encoding.UTF8.GetString(e!.Value.Value.Span).Should().Be("v2");
    }

    [Fact]
    public async Task should_keep_multiple_versions_given_dedupe_disabled_when_sealing()
    {
        // Arrange
        var f = CreateFactory(false);

        // Act
        await using (var w = f.CreateWriter("/versions", 2))
        {
            static async IAsyncEnumerable<DbEntry> Data()
            {
                await Task.Yield();
                yield return E("k", "v1", 1);
                yield return E("k", "v2", 2);
            }

            await w.WriteAsync(Data());
        }

        // Assert
        var r = f.CreateReader("/versions");
        var list = new List<DbEntry>();
        await foreach (var d in r.ReadAllAsync()) list.Add(d);
        list.Count(x => Encoding.UTF8.GetString(x.Key.Span) == "k").Should().Be(2);
    }
}
