using CleanArchitecture.Application.Common.Models;
using Microsoft.EntityFrameworkCore;

namespace CleanArchitecture.Infrastructure.Data.Pagination;

/// <summary>
/// Reads one offset page from an EF query. It lives here to keep query execution in Infrastructure: Application
/// describes the page it wants with <see cref="PaginationQuery"/> and receives a <see cref="PaginatedList{T}"/>, but
/// never runs a query itself.
/// </summary>
public static class PaginationExtensions
{
    /// <summary>
    /// Counts the whole query, then reads the requested page of it. These are two queries without a snapshot
    /// transaction: directories are advisory reads, so a count that briefly disagrees with its page is absorbed by
    /// the past-the-end correction and the next refresh.
    /// <para>
    /// The helper never orders. The caller orders by a unique key first; without that order, consecutive pages have
    /// no stable row order and may repeat or skip rows.
    /// </para>
    /// </summary>
    public static async Task<PaginatedList<T>> ToPaginatedListAsync<T>(
        this IQueryable<T> query,
        PaginationQuery pagination,
        CancellationToken cancellationToken)
    {
        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip(pagination.Skip)
            .Take(pagination.PageSize)
            .ToListAsync(cancellationToken);

        return new PaginatedList<T>(items, pagination.PageNumber, pagination.PageSize, totalCount);
    }
}
