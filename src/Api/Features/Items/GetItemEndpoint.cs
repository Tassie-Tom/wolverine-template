using Api.Data;
using Microsoft.EntityFrameworkCore;
using Wolverine.Http;

namespace Api.Features.Items;

public static class GetItemEndpoint
{
    public static async Task<ItemResponse?> LoadAsync(
        Guid id,
        AppDbContext db)
    {
        return await db.Items
            .Where(i => i.Id == id)
            .Select(i => new ItemResponse(
                i.Id,
                i.Name,
                i.Description,
                i.Price,
                i.CreatedByUserId,
                i.CreatedAt
            ))
            .FirstOrDefaultAsync();
    }

    [Tags("Items")]
    [WolverineGet("/api/v1/items/{id}")]
    public static ItemResponse Handle(Guid id, ItemResponse item)
    {
        return item;
    }
}
