using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Gravel.Abstractions;
using Gravel.Abstractions.Storage.Wal;
using Microsoft.Extensions.Options;
using Xunit;

namespace Gravel.Storage.InMemory.Wal;

public class InMemoryWalReaderTests
{
    static IWalFactory Factory(int max = 100, bool shared = true) =>
        new InMemoryWalFactory(Options.Create(new InMemoryWalOptions { MaxBufferedRecords = max, SharedWriter = shared }));

    [Fact]
    public async Task should_replay_records_in_order_given_enqueued_transactions_when_replay()
    {
        // Arrange
        var f = Factory();
        var w = f.CreateWriter("/db");
        await w.BeginTransactionAsync(10);
        await w.AppendAsync(10, DbEntry.Put("a"u8.ToArray(), "1"u8.ToArray(), 0));
        await w.AppendAsync(10, DbEntry.Put("b"u8.ToArray(), "2"u8.ToArray(), 0));
        await w.CommitTransactionAsync(10);

        // Act
        await using var r = f.CreateReader("/db");
        var list = new List<WalRecord>();
        await foreach (var rec in r.ReplayAsync()) list.Add(rec);

        // Assert
        list.Select(x => x.Type).Should()
            .ContainInOrder(WalConstants.RecordBeginTxn, WalConstants.RecordEntry, WalConstants.RecordEntry,
                WalConstants.RecordCommitTxn);
    }

    [Fact]
    public async Task should_truncate_given_exceeding_max_buffer_when_replay()
    {
        // Arrange
        var f = Factory(5);
        var w = f.CreateWriter("/t");
        for (ulong i = 0; i < 20; i++)
        {
            await w.BeginTransactionAsync(i);
            await w.AppendAsync(i, DbEntry.Put(BitConverter.GetBytes(i), BitConverter.GetBytes(i), 0));
            await w.CommitTransactionAsync(i);
        }

        // Act
        await using var r = f.CreateReader("/t");
        var count = 0;
        await foreach (var _ in r.ReplayAsync()) count++;

        // Assert
        count.Should().BeLessThanOrEqualTo(5);
    }
}