using Tavi.Extensibility;
using Xunit;

namespace Tavi.Application.Tests;

/// <summary>验证 Module 公共协议与 Domain/Application 依赖方向。</summary>
public sealed class ArchitectureTests
{
    /// <summary>验证 Extensibility 不引用 Domain 或 Application。</summary>
    [Fact]
    public void ExtensibilityHasNoDomainOrApplicationReference()
    {
        string[] references = typeof(ITaviPlugin).Assembly.GetReferencedAssemblies().Select(reference => reference.Name!).ToArray();
        Assert.DoesNotContain("Tavi.Domain", references);
        Assert.DoesNotContain("Tavi.Application", references);
    }

    /// <summary>验证 Domain 不引用 Extensibility 或 Application。</summary>
    [Fact]
    public void DomainHasNoExtensibilityOrApplicationReference()
    {
        string[] references = typeof(Tavi.Domain.World.World).Assembly.GetReferencedAssemblies().Select(reference => reference.Name!).ToArray();
        Assert.DoesNotContain("Tavi.Extensibility", references);
        Assert.DoesNotContain("Tavi.Application", references);
    }

    /// <summary>验证旧 Registrar 接口已经从公共程序集删除。</summary>
    [Fact]
    public void RegistrarNoLongerExists() => Assert.DoesNotContain(typeof(ITaviPlugin).Assembly.GetExportedTypes(), type => type.Name == "IPluginRegistrar");
}
