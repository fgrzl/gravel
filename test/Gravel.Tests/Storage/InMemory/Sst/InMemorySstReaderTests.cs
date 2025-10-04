using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Gravel.Abstractions;
using Gravel.Abstractions.Storage.Sst;
using Microsoft.Extensions.Options;
using Xunit;

namespace Gravel.Storage.InMemory.Sst;

public class InMemorySstReaderTests
{
    static InMemorySstFactory CreateFactory()
    {
        return new InMemorySstFactory(Options.Create(new InMemorySstOptions()));
    }

    static DbEntry E(string k, string v, ulong seq)
    {
        return DbEntry.Put(Encoding.UTF8.GetBytes(k), Encoding.UTF8.GetBytes(v), seq);
    }

    static async Task WriteAsync(ISstFactory f, string path, params (string k, string v)[] kv)
    {
        await using var w = f.CreateWriter(path, kv.Length);
        await w.WriteAsync(Data());
        return;

        async IAsyncEnumerable<DbEntry> Data()
        {
            ulong seq = 1;
            foreach (var (k, v) in kv)
            {
                yield return E(k, v, seq++);
                await Task.Yield();
            }
        }
    }

    [Fact]
    public async Task should_return_null_given_missing_key_when_get_async()
    {
        // Arrange
        var f = CreateFactory();
        await WriteAsync(f, "/r1", ("a", "1"));
        var r = f.CreateReader("/r1");

        // Act
        var got = await r.GetAsync("nope"u8.ToArray());

        // Assert
        got.Should().BeNull();
    }

    [Fact]
    public async Task should_stream_all_entries_given_multiple_puts_when_read_all()
    {
        // Arrange
        var f = CreateFactory();
        await WriteAsync(f, "/r2", ("a", "1"), ("b", "2"), ("c", "3"));
        var r = f.CreateReader("/r2");

        // Act
        var list = new List<DbEntry>();
        await foreach (var d in r.ReadAllAsync()) list.Add(d);

        // Assert
        list.Select(e => Encoding.UTF8.GetString(e.Key.Span)).Should().BeEquivalentTo("a", "b", "c");
    }
}
