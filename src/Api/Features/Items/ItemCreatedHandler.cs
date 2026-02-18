using Api.Domain.Events;

namespace Api.Features.Items;

public static class ItemCreatedHandler
{
    public static void Handle(ItemCreated @event, ILogger logger)
    {
        logger.LogInformation(
            "Item created event processed: {ItemId} \"{Name}\" (${Price}) by user {UserId}",
            @event.ItemId,
            @event.Name,
            @event.Price,
            @event.CreatedByUserId
        );
    }
}
