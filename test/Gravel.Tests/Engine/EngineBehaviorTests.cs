using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Gravel.Abstractions;
using Gravel.Abstractions.Storage.Sst;
using Gravel.Abstractions.Storage.Wal;
using Gravel.Engine.Compaction;
using Gravel.Storage.InMemory.Sst;
using Gravel.Storage.InMemory.Wal;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using Gravel.Tests.TestHelpers;

namespace Gravel.Engine.Tests
{
    public class EngineBehaviorTests
    {
        static ReadOnlyMemory<byte> B(string s) => Encoding.UTF8.GetBytes(s);

        static IGravelEngine CreateEngine(ISstFactory sstFactory, IWalFactory walFactory, GravelOptions? opts = null)
        {
            var options = Options.Create(opts ?? new GravelOptions
            {
                DatabasePath = "mem://db",
                MemTableThreshold = 100,
                SstLevels = 3,
                WalSyncOnCommit = true
            });

            var worker = new CompactionWorker(double.MaxValue);
            return new Gravel.Engine.DbEngine(options, walFactory, sstFactory, worker, NullLogger<Gravel.Engine.DbEngine>.Instance);
        }

        [Theory]
        [MemberData(nameof(EngineTestProviders.AllEngineProviders), MemberType = typeof(EngineTestProviders))]
        public async Task should_roundtrip_put_get_delete_given_basic_flow(string mode, ISstFactory sstFactory, IWalFactory walFactory, string tempDir)
        {
            // Arrange
            await using var eng = CreateEngine(sstFactory, walFactory);
            await eng.InitializeAsync();

            var key = B("k1");
            var val = B("v1");

            // Act
            await eng.PutAsync(key, val);
            var got = await eng.GetAsync(key);
            var existed = await eng.ExistsAsync(key);
            var deleted = await eng.DeleteAsync(key);
            var missing = await eng.GetAsync(key);

            // Assert
            got.HasValue.Should().BeTrue();
            got!.Value.ToArray().Should().Equal(val.ToArray());
            existed.Should().BeTrue();
            deleted.Should().BeTrue();
            missing.Should().BeNull();

            // cleanup for filesystem
            if (!string.IsNullOrEmpty(tempDir))
            {
                try { System.IO.Directory.Delete(tempDir, true); } catch { }
            }
        }
    }
}
