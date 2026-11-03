using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Aggregator.WebApi.Product;

public sealed class ProductService : IProductService
{
    private readonly HttpClient _httpClient;
    private readonly string _productServiceUrl;

    public ProductService(IHttpClientFactory httpClientFactory, IConfiguration configuration)
    {
        _httpClient = httpClientFactory.CreateClient();
        _productServiceUrl = configuration["MicroserviceUrls:ProductService"] ?? "http://localhost:5003";
    }

    public async Task<ProductApi> GetProductByIdAsync(int productId, System.Threading.CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync($"{_productServiceUrl.TrimEnd('/')}/api/v1/Product/{productId}", cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return JsonConvert.DeserializeObject<ProductApi>(await response.Content.ReadAsStringAsync(cancellationToken));
    }
}
