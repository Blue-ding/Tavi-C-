using Tavi.Domain.World;
using Xunit;

namespace Tavi.Domain.Tests;

/// <summary>验证 World 开放类型键的稳定身份和输入约定。</summary>
public sealed class WorldTypeTests
{
    /// <summary>验证四种名义类型都提供相同文本的独立内置 none 值。</summary>
    [Fact]
    public void NoneTypesUseStableCoreKey()
    {
        Assert.Equal("core:none", ElementType.None.Value);
        Assert.Equal("core:none", AspectType.None.Value);
        Assert.Equal("core:none", RelationType.None.Value);
        Assert.Equal("core:none", ScopeType.None.Value);
    }

    /// <summary>验证 Module 命名空间键保持大小写敏感的稳定身份。</summary>
    [Fact]
    public void ModuleTypeKeysAreCaseSensitive()
    {
        var lower = new ElementType("module:character");
        var upper = new ElementType("module:Character");

        Assert.NotEqual(lower, upper);
        Assert.Equal("module:character", lower.ToString());
    }

    /// <summary>验证默认结构值保持未初始化，而不会被误解释为 core:none。</summary>
    [Fact]
    public void DefaultTypeIsUninitialized()
    {
        ElementType type = default;

        Assert.True(type.IsEmpty);
        Assert.Equal(string.Empty, type.ToString());
        Assert.NotEqual(ElementType.None, type);
    }

    /// <summary>验证不符合稳定命名约定的类型键在构造时被拒绝。</summary>
    /// <param name="value">预期无效的类型键文本。</param>
    [Theory]
    [InlineData("")]
    [InlineData("none")]
    [InlineData(":none")]
    [InlineData("core:")]
    [InlineData(" core:none")]
    [InlineData("core:none ")]
    public void InvalidTypeKeyIsRejected(string value)
    {
        Assert.Throws<ArgumentException>(() => new ElementType(value));
    }
}
