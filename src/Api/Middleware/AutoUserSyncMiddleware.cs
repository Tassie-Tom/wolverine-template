using Api.Data;
using Api.Domain.Entities;
using Api.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Serilog.Context;
using System.Collections.Concurrent;

namespace Api.Middleware;

public class AutoUserSyncMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IMemoryCache _cache;
    private readonly ILogger<AutoUserSyncMiddleware> _logger;
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> _userLocks = new();

    public AutoUserSyncMiddleware(
        RequestDelegate next,
        IMemoryCache cache,
        ILogger<AutoUserSyncMiddleware> logger)
    {
        _next = next;
        _cache = cache;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, AppDbContext db)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var userId = context.User.GetUserId();

            if (userId.HasValue)
            {
                var cacheKey = $"user_exists_{userId.Value}";

                await _cache.GetOrCreateAsync(cacheKey, async entry =>
                {
                    var exists = await db.Users.AnyAsync(u => u.Id == userId.Value);

                    if (!exists)
                    {
                        var userLock = _userLocks.GetOrAdd(userId.Value, _ => new SemaphoreSlim(1, 1));
                        await userLock.WaitAsync();
                        try
                        {
                            exists = await db.Users.AnyAsync(u => u.Id == userId.Value);

                            if (!exists)
                            {
                                var created = await TrySyncUserFromJwt(context, db, userId.Value);

                                if (!created)
                                {
                                    entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(10);
                                    return false;
                                }

                                exists = true;
                            }
                        }
                        finally
                        {
                            userLock.Release();
                            _userLocks.TryRemove(userId.Value, out _);
                        }
                    }

                    entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1);
                    return exists;
                });

                using (LogContext.PushProperty("UserId", userId.Value))
                {
                    await _next(context);
                    return;
                }
            }
        }

        await _next(context);
    }

    private async Task<bool> TrySyncUserFromJwt(HttpContext context, AppDbContext db, Guid userId)
    {
        try
        {
            var email = context.User.GetUserEmail();

            var userMetadataClaim = context.User.FindFirst("user_metadata")?.Value;

            string firstName = "User";
            string lastName = "";

            if (!string.IsNullOrEmpty(userMetadataClaim))
            {
                try
                {
                    var metadata = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(userMetadataClaim);
                    if (metadata != null)
                    {
                        if (metadata.TryGetValue("first_name", out var fn))
                            firstName = fn?.ToString() ?? "User";
                        if (metadata.TryGetValue("last_name", out var ln))
                            lastName = ln?.ToString() ?? "";
                    }
                }
                catch (Exception parseEx)
                {
                    _logger.LogWarning(parseEx, "Failed to parse user_metadata from JWT for user {UserId}", userId);
                }
            }

            var user = User.Create(
                userId,
                email ?? $"{userId}@placeholder.local",
                firstName,
                lastName
            );

            db.Users.Add(user);
            await db.SaveChangesAsync();

            _logger.LogDebug(
                "Auto-synced user {UserId} ({Email}) from JWT",
                userId,
                email
            );

            var cacheKey = $"user_exists_{userId}";
            _cache.Set(cacheKey, true, TimeSpan.FromHours(1));

            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
        {
            _logger.LogDebug("User {UserId} already exists (race condition handled)", userId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to auto-sync user {UserId} from JWT",
                userId
            );
            return false;
        }
    }
}
