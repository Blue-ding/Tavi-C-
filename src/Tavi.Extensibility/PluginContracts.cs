namespace Tavi.Extensibility;

/// <summary>定义一个可选的代码 Plugin 入口；纯声明式 Module 不需要实现该接口。</summary>
public interface ITaviPlugin
{
    /// <summary>获取 Plugin 所属的主 Module。</summary>
    ModuleId Module { get; }

    /// <summary>获取 Plugin 与声明式 Module 一致的兼容版本。</summary>
    ModuleVersion Version { get; }

}
