using NewsCMS.Application.Catalog.Dtos;

namespace NewsCMS.Theme.PhuPhucYaka.Areas.PhuPhucYaka.Controllers;

public sealed record HomeViewModel(
    IReadOnlyList<ProductListItemDto> Featured);
