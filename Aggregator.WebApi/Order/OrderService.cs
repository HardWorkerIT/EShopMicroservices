using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Aggregator.WebApi.Order;

public sealed class OrderService : IOrderService
{
    private readonly HttpClient _httpClient;
    private readonly string _orderServiceUrl;

    public OrderService(IHttpClientFactory httpClientFactory, IConfiguration configuration)
    {
        _httpClient = httpClientFactory.CreateClient();
        _orderServiceUrl = configuration["MicroserviceUrls:OrderService"] ?? "http://localhost:5002";
    }

    public async Task<List<OrderApi>> GetOrdersByCustomerIdAsync(int customerId, System.Threading.CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync($"{_orderServiceUrl.TrimEnd('/')}/api/v1/Order/Customers/{customerId}", cancellationToken);
        response.EnsureSuccessStatusCode();
        return JsonConvert.DeserializeObject<List<OrderApi>>(await response.Content.ReadAsStringAsync(cancellationToken)) ?? new List<OrderApi>();
    }
}
