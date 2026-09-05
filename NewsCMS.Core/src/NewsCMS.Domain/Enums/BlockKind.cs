namespace NewsCMS.Domain.Enums;

/// <summary>Khối builder: tĩnh (HTML dựng sẵn) hoặc động (render server theo handler).</summary>
public enum BlockKind
{
    Static = 0,
    /// <summary>Khối động — render bằng IDynamicBlock theo DynamicHandler (post-list, product-grid...).</summary>
    Dynamic = 1
}
