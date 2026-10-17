using Microsoft.Extensions.Logging;

namespace Modulix.ReverseProxy.Services;

public class ReverseProxyService : IReverseProxyService
{
    private readonly ILogger<ReverseProxyService> _logger;

    public ReverseProxyService(ILogger<ReverseProxyService> logger)
    {
        _logger = logger;
    }
}