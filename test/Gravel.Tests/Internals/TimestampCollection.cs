using Xunit;

namespace Gravel.Internals;

[CollectionDefinition("TimestampTests")]
public class TimestampCollection : ICollectionFixture<TimestampTestFixture>
{
    // This class has no code, and is never created. Its purpose is simply
    // to be the place to apply [CollectionDefinition] and the
    // ICollectionFixture<> interfaces.
}
