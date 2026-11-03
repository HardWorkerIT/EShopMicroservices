using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Aggregator.WebApi.Customer;

public sealed class CustomerService : ICustomerService
{
    private readonly HttpClient _httpClient;
    private readonly string _customerServiceUrl;

    public CustomerService(IHttpClientFactory httpClientFactory, IConfiguration configuration)
    {
        _httpClient = httpClientFactory.CreateClient();
        _customerServiceUrl = configuration["MicroserviceUrls:CustomerService"] ?? "http://localhost:5001";
    }

    public async Task<CustomerApi> GetCustomerByNameAsync(string customerName, System.Threading.CancellationToken cancellationToken)
    {
        var url = $"{_customerServiceUrl.TrimEnd('/')}/api/v1/Customer/Nickname/{Uri.EscapeDataString(customerName)}";
        using var response = await _httpClient.GetAsync(url, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return JsonConvert.DeserializeObject<CustomerApi>(await response.Content.ReadAsStringAsync(cancellationToken));
    }
}
