using Microsoft.EntityFrameworkCore;
using PersonalPlatform.Contracts;

namespace PersonalPlatform.Application.Common;

public static class PagingExtensions
{
    /// <summary>
    /// Applies bounded pagination. Page and size are validated at the API boundary; they are clamped here
    /// again so that no code path can ever request an unbounded page.
    /// </summary>
    public static async Task<PagedResult<T>> ToPagedResultAsync<T>(this IQueryable<T> query, int page, int pageSize, CancellationToken cancellationToken)
    {
        var size = Math.Clamp(pageSize, 1, Paging.MaxPageSize);
        var number = Math.Clamp(page, 1, Paging.MaxPage);
        var total = await query.CountAsync(cancellationToken);
        var items = await query.Skip((number - 1) * size).Take(size).ToListAsync(cancellationToken);
        return new PagedResult<T>(items, number, size, total);
    }
}
