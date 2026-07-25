namespace Tavi.Extensibility;

/// <summary>表示跨聚合公开的只读 Element 投影。</summary>
/// <param name="Id">Element 标识。</param><param name="Name">名称。</param><param name="Description">说明。</param><param name="Type">规则化类型。</param>
public sealed record ElementView(Guid Id, string Name, string Description, SemanticKey Type);

/// <summary>表示跨聚合公开的只读 Scope 投影。</summary>
/// <param name="Id">Scope 标识。</param><param name="Quantity">整数数量。</param><param name="Type">规则化类型。</param><param name="OwnerElementId">Owner Element 标识。</param>
public sealed record ScopeView(Guid Id, int Quantity, SemanticKey Type, Guid OwnerElementId);

/// <summary>表示跨聚合公开的只读 Aspect 投影。</summary>
/// <param name="Id">Aspect 标识。</param><param name="Quantity">整数数量。</param><param name="Type">规则化类型。</param><param name="ElementId">目标 Element 标识。</param><param name="ScopeId">唯一 Scope 标识。</param>
public sealed record AspectView(Guid Id, int Quantity, SemanticKey Type, Guid ElementId, Guid ScopeId);

/// <summary>表示跨聚合公开的只读 Relation 投影。</summary>
/// <param name="Id">Relation 标识。</param><param name="Quantity">整数数量。</param><param name="Type">规则化类型。</param><param name="SourceElementId">来源 Element 标识。</param><param name="TargetElementId">目标 Element 标识。</param><param name="ScopeId">唯一 Scope 标识。</param>
public sealed record RelationView(Guid Id, int Quantity, SemanticKey Type, Guid SourceElementId, Guid TargetElementId, Guid ScopeId);
