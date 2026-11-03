using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Polly.Timeout;

namespace Aggregator.WebApi.HealthChecks;

public sealed class GatewayDependenciesHealthCheck : IHealthCheck
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;

    public GatewayDependenciesHealthCheck(IHttpClientFactory httpClientFactory, IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var dependencies = new[]
        {
            (Name: "Customer", Url: _configuration["MicroserviceUrls:CustomerService"] ?? "http://localhost:5001"),
            (Name: "Order", Url: _configuration["MicroserviceUrls:OrderService"] ?? "http://localhost:5002"),
            (Name: "Product", Url: _configuration["MicroserviceUrls:ProductService"] ?? "http://localhost:5003")
        };

        var results = await Task.WhenAll(dependencies.Select(async dependency =>
        {
            try
            {
                using var response = await _httpClientFactory.CreateClient().GetAsync(
                    $"{dependency.Url.TrimEnd('/')}/health/ready", cancellationToken);
                return (dependency.Name, response.IsSuccessStatusCode);
            }
            catch (Exception exception) when (exception is HttpRequestException or TimeoutRejectedException)
            {
                return (dependency.Name, false);
            }
        }));

        var unavailable = results.Where(result => !result.Item2).Select(result => result.Name).ToArray();
        return unavailable.Length == 0
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy($"Dependencies not ready: {string.Join(", ", unavailable)}.");
    }
}
