namespace CleanArchitecture.Application.Common.Models;

/// <summary>
/// One offset page request. Out-of-range values are clamped rather than refused: a page outside the range is a
/// client bug, not a security event, and no input may turn into an unexpected failure (E1, E2).
/// </summary>
public sealed record PaginationQuery(int PageNumber = 1, int PageSize = 25)
{
    public const int DefaultPageNumber = 1;
    public const int DefaultPageSize = 25;
    public const int MaxPageSize = 100;

    /// <summary>The largest page whose <see cref="Skip"/> still fits in an <see cref="int"/> at any page size.</summary>
    public const int MaxPageNumber = int.MaxValue / MaxPageSize;

    public static PaginationQuery Default { get; } = new();

    public int PageNumber { get; } = Math.Clamp(PageNumber, DefaultPageNumber, MaxPageNumber);

    /// <summary>Clamped to 1–<see cref="MaxPageSize"/>, so a size of zero or less means one row (PD-1).</summary>
    public int PageSize { get; } = Math.Clamp(PageSize, 1, MaxPageSize);

    // `checked` stays as a tripwire: with both clamps above it cannot fire, and a regression becomes loud.
    public int Skip => checked((PageNumber - 1) * PageSize);

    /// <summary>
    /// Builds the page a list route was asked for. An omitted value is the default page, never every row.
    /// Non-integer and beyond-Int32 values never reach here: binding refuses them as <c>invalid_request</c>.
    /// </summary>
    public static PaginationQuery From(int? pageNumber, int? pageSize) =>
        new(pageNumber ?? DefaultPageNumber, pageSize ?? DefaultPageSize);
}
