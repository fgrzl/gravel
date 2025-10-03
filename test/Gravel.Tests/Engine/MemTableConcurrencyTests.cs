using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Gravel.Internals;
using Xunit;

namespace Gravel.Engine;

public class MemTableConcurrencyTests
{
    static ReadOnlyMemory<byte> B(string s)
    {
        return Encoding.UTF8.GetBytes(s);
    }

    [Fact]
    public async Task should_support_safe_scan_under_concurrent_puts_and_deletes()
    {
        var mt = new MemTable();
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        // Writer task: mutate memtable
        var writer = Task.Run(async () =>
        {
            var i = 0;
            while (!cts.IsCancellationRequested)
            {
                var k = B("k" + (i % 50));
                var v = B("v" + i);
                if (i % 10 == 0)
                    mt.PutDeleteTombstone(k.Span, (ulong)i);
                else if (i % 15 == 0)
                    mt.PutRangeTombstone(B("a").Span, B("z").Span, (ulong)i);
                else
                    mt.Put(k.Span, v.Span, (ulong)i);
                i++;
                await Task.Yield();
            }
        }, cts.Token);

        // Reader loop: repeatedly scan; should never throw
        var scans = 0;
        var lastNonEmpty = false;
        while (scans < 50)
        {
            var list = mt.Scan().ToList();
            // basic sanity: keys must be non-decreasing
            for (var i = 1; i < list.Count; i++)
                ByteComparer.Compare(list[i - 1].Key.Span, list[i].Key.Span).Should().BeLessOrEqualTo(0);
            lastNonEmpty = lastNonEmpty || list.Count > 0;
            scans++;
            await Task.Yield();
        }

        cts.Cancel();
        await Task.WhenAny(writer, Task.Delay(100));

        lastNonEmpty.Should().BeTrue();
    }
}