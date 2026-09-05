using NewsCMS.Application.Content.Dtos;

namespace NewsCMS.Theme.HaiLuuNguoc.Areas.Theme.Controllers;

public sealed record HomeViewModel(
    IReadOnlyList<PostListItemDto> Featured,
    IReadOnlyList<PostListItemDto> Latest);
