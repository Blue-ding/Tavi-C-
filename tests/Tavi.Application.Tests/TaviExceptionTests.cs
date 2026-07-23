using Tavi.Application.Guidance;
using Tavi.Application.LanguageModel;
using Xunit;

namespace Tavi.Application.Tests;

/// <summary>验证跨模块异常契约的稳定性约定。</summary>
public sealed class TaviExceptionTests
{
    [Theory]
    [InlineData("")]
    [InlineData("guidance.session_not_found")]
    [InlineData("TAVI.GUIDANCE.INVALID")]
    [InlineData("TAVI.Guidance.SESSION.NOT_FOUND")]
    public void InvalidErrorCodeConventionIsRejected(string errorCode)
    {
        Assert.Throws<ArgumentException>(() => new LanguageModelException(errorCode, TaviErrorCategory.InternalFailure, "失败。"));
    }

    [Fact]
    public void DiagnosticDetailsAreCopiedAndReadOnly()
    {
        var source = new Dictionary<string, string> { ["State"] = "Running" };
        var exception = new GuidanceException(GuidanceErrorCodes.InvalidSessionState, "Continue", "状态无效。", details: source);

        source["State"] = "Changed";

        Assert.Equal("Running", exception.Details["State"]);
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, string>)exception.Details).Add("Other", "Value"));
    }
}
