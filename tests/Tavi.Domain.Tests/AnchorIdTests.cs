using Tavi.Domain.WorldGraph;
using Xunit;

namespace Tavi.Domain.Tests;

public sealed class AnchorIdTests
{
    [Fact]
    public void NewIdsAreUnique()
    {
        var first = new AnchorId();
        var second = new AnchorId();

        Assert.NotEqual(first, second);
    }
}
