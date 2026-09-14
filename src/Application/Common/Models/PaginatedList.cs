namespace CleanArchitecture.Application.Common.Models;

/// <summary>
/// One offset page of results with its navigation metadata. It is pure Application code with no EF dependency:
/// Infrastructure executes the query, and Web maps the page into an endpoint-specific DTO, never an envelope.
/// </summary>
public sealed record PaginatedList<T>(
    IReadOnlyList<T> Items,
    int PageNumber,
    int PageSize,
    int TotalCount)
{
    public int TotalPages => TotalCount == 0
        ? 0
        : (int)Math.Ceiling(TotalCount / (double)PageSize);

    public bool HasPreviousPage => PageNumber > 1 && TotalPages > 0;

    public bool HasNextPage => PageNumber < TotalPages;
}
