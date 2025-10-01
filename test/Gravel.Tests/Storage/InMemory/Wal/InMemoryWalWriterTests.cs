using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Gravel.Abstractions;
using Gravel.Abstractions.Storage.Wal;
using Microsoft.Extensions.Options;
using Xunit;

namespace Gravel.Storage.InMemory.Wal;

public class InMemoryWalWriterTests
{
    static IWalFactory Factory(int max = 1000, bool shared = true) =>
        new InMemoryWalFactory(Options.Create(new InMemoryWalOptions { MaxBufferedRecords = max, SharedWriter = shared }));

    [Fact]
    public async Task should_increment_last_sequence_given_multiple_appends_when_commit()
    {
        // Arrange
        var f = Factory();
        var w = f.CreateWriter("/mem");

        // Act
        await w.BeginTransactionAsync(1);
        await w.AppendAsync(1, DbEntry.Put("a"u8.ToArray(), "1"u8.ToArray(), 0));
        await w.AppendAsync(1, DbEntry.Put("b"u8.ToArray(), "2"u8.ToArray(), 0));
        await w.CommitTransactionAsync(1);

        // Assert
        w.LastSequence.Should().Be(2);
    }

    [Fact]
    public async Task should_record_rollback_given_transaction_rollback_when_replay()
    {
        // Arrange
        var f = Factory();
        var w = f.CreateWriter("/mem");

        // Act
        await w.BeginTransactionAsync(7);
        await w.AppendAsync(7, DbEntry.Put("k"u8.ToArray(), "v"u8.ToArray(), 0));
        await w.RollbackTransactionAsync(7);

        // Assert
        await using var r = f.CreateReader("/mem");
        var list = new List<WalRecord>();
        await foreach (var rec in r.ReplayAsync()) list.Add(rec);
        list.Should().HaveCount(3);
        list[0].Type.Should().Be(WalConstants.RecordBeginTxn);
        list[1].Type.Should().Be(WalConstants.RecordEntry);
        list[2].Type.Should().Be(WalConstants.RecordRollbackTxn);
    }
}