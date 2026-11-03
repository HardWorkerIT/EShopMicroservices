using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using System;
using Microsoft.Extensions.Configuration;

namespace Order.Microservice.Clients;

public sealed class ProductInventoryClient : IProductInventoryClient
{
    private readonly HttpClient _httpClient;

    public ProductInventoryClient(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _httpClient.BaseAddress = new System.Uri(configuration["MicroserviceUrls:ProductService"] ?? "http://localhost:5003");
        _httpClient.DefaultRequestHeaders.Add("X-Service-Key", configuration["ServiceAuth:Key"] ?? string.Empty);
    }

    public async Task<HttpStatusCode> ReserveAsync(long productId, int quantity, Guid reservationId, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync($"/api/v1/Product/{productId}/reserve", new { quantity, reservationId }, cancellationToken);
        return response.StatusCode;
    }

    public async Task ReleaseAsync(long productId, int quantity, Guid reservationId, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync($"/api/v1/Product/{productId}/release", new { quantity, reservationId }, cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}
