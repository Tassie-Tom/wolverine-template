using Microsoft.IdentityModel.Tokens;

namespace Api.Services;

public class SupabaseJwksService : IAsyncDisposable
{
    private readonly IConfiguration _configuration;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<SupabaseJwksService> _logger;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    private List<SecurityKey> _signingKeys = new();
    private DateTimeOffset _lastRefresh = DateTimeOffset.MinValue;
    private readonly TimeSpan _refreshInterval = TimeSpan.FromHours(24);

    public SupabaseJwksService(
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory,
        ILogger<SupabaseJwksService> logger)
    {
        _configuration = configuration;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<IEnumerable<SecurityKey>> GetSigningKeysAsync()
    {
        if (_signingKeys.Count == 0 || DateTimeOffset.UtcNow - _lastRefresh > _refreshInterval)
        {
            await RefreshKeysAsync();
        }

        return _signingKeys;
    }

    private async Task RefreshKeysAsync()
    {
        await _refreshLock.WaitAsync();
        try
        {
            if (_signingKeys.Count > 0 && DateTimeOffset.UtcNow - _lastRefresh <= _refreshInterval)
            {
                return;
            }

            var supabaseUrl = _configuration["Supabase:Url"]
                ?? throw new InvalidOperationException("Supabase:Url is required");

            var jwksUrl = $"{supabaseUrl}/auth/v1/.well-known/jwks.json";

            var httpClient = _httpClientFactory.CreateClient();
            var jwksJson = await httpClient.GetStringAsync(jwksUrl);
            var jwks = new JsonWebKeySet(jwksJson);

            _signingKeys = jwks.GetSigningKeys().ToList();
            _lastRefresh = DateTimeOffset.UtcNow;

            _logger.LogInformation(
                "JWKS keys refreshed successfully. {KeyCount} keys loaded from {Url}",
                _signingKeys.Count,
                jwksUrl
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh JWKS keys");

            if (_signingKeys.Count == 0)
            {
                throw;
            }
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        _refreshLock.Dispose();
        await ValueTask.CompletedTask;
    }
}
