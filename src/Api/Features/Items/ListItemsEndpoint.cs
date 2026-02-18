using Api.Data;
using Microsoft.EntityFrameworkCore;
using Wolverine.Http;

namespace Api.Features.Items;

public static class ListItemsEndpoint
{
    [Tags("Items")]
    [WolverineGet("/api/v1/items")]
    public static async Task<IReadOnlyList<ItemResponse>> Handle(
        AppDbContext db,
        string? search = null)
    {
        var query = db.Items.AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(i => i.Name.Contains(search));
        }

        return await query
            .OrderByDescending(i => i.CreatedAt)
            .Select(i => new ItemResponse(
                i.Id,
                i.Name,
                i.Description,
                i.Price,
                i.CreatedByUserId,
                i.CreatedAt
            ))
            .ToListAsync();
    }
}
