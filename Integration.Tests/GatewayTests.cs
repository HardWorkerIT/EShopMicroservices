using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using Aggregator.WebApi;
using Aggregator.WebApi.Customer;
using Aggregator.WebApi.Order;
using Aggregator.WebApi.Product;
using Polly.Timeout;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Xunit;

public class GatewayTests
{
    [Fact]
    public async Task Unknown_customer_returns_404()
    {
        using var host = await StartHost(new CustomerStub(null));
        using var client = CreateClient(host);
        using var response = await client.GetAsync("/api/v1/Shop/Customers/missing");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Downstream_failure_returns_502_without_internal_details()
    {
        using var host = await StartHost(new CustomerStub(null, true));
        using var client = CreateClient(host);
        using var response = await client.GetAsync("/api/v1/Shop/Customers/example");
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.DoesNotContain("private upstream detail", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Downstream_timeout_returns_504()
    {
        using var host = await StartHost(new CustomerStub(null, timeout: true));
        using var client = CreateClient(host);
        using var response = await client.GetAsync("/api/v1/Shop/Customers/example");

        Assert.Equal(HttpStatusCode.GatewayTimeout, response.StatusCode);
    }

    [Fact]
    public async Task Unhandled_exception_returns_sanitized_problem_details_with_trace_id()
    {
        using var host = await StartHost(new CustomerStub(null, crash: true));
        using var client = CreateClient(host);
        using var response = await client.GetAsync("/api/v1/Shop/Customers/example");
        var body = await response.Content.ReadAsStringAsync();
        using var problem = JsonDocument.Parse(body);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.DoesNotContain("private exception detail", body);
        Assert.False(string.IsNullOrWhiteSpace(problem.RootElement.GetProperty("traceId").GetString()));
    }

    [Fact]
    public async Task Customer_orders_are_composed_with_products()
    {
        using var host = await StartHost(
            new CustomerStub(new CustomerApi { Id = 42 }, delay: TimeSpan.FromMilliseconds(75)),
            new OrderStub(new OrderApi { CustomerId = 42, ProductId = 7 }),
            new ProductStub(new ProductApi { Id = 7, Name = "Keyboard", Rate = 99 }));
        using var client = CreateClient(host);
        using var response = await client.GetAsync("/api/v1/Shop/Customers/example");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Keyboard", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Request_cancellation_is_forwarded_to_downstream_service()
    {
        var customer = new CustomerStub(null, waitForCancellation: true);
        using var host = await StartHost(customer);
        using var client = CreateClient(host);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.GetAsync("/api/v1/Shop/Customers/example", cancellation.Token));
        Assert.True(await customer.CancellationObserved.WaitAsync(TimeSpan.FromSeconds(2)));
    }

    private static async Task<IHost> StartHost(
        ICustomerService customer,
        IOrderService order = null,
        IProductService product = null)
    {
        var host = Host.CreateDefaultBuilder().ConfigureWebHostDefaults(web =>
        {
            web.UseUrls("http://127.0.0.1:0");
            web.UseStartup<Startup>();
            web.ConfigureServices(services =>
            {
                services.RemoveAll<ICustomerService>();
                services.RemoveAll<IOrderService>();
                services.RemoveAll<IProductService>();
                services.AddSingleton(customer);
                services.AddSingleton(order ?? new OrderStub());
                services.AddSingleton(product ?? new ProductStub());
            });
        }).Build();
        await host.StartAsync();
        return host;
    }

    private static HttpClient CreateClient(IHost host)
    {
        var addresses = host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>();
        return new HttpClient { BaseAddress = new Uri(System.Linq.Enumerable.Single(addresses.Addresses)) };
    }

    private sealed class CustomerStub : ICustomerService
    {
        private readonly CustomerApi _customer;
        private readonly bool _fail;
        private readonly bool _timeout;
        private readonly bool _crash;
        private readonly bool _waitForCancellation;
        private readonly TimeSpan _delay;
        private readonly TaskCompletionSource<bool> _cancellationObserved = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<bool> CancellationObserved => _cancellationObserved.Task;
        public CustomerStub(CustomerApi customer, bool fail = false, bool timeout = false, bool crash = false, bool waitForCancellation = false, TimeSpan delay = default)
        { _customer = customer; _fail = fail; _timeout = timeout; _crash = crash; _waitForCancellation = waitForCancellation; _delay = delay; }
        public async Task<CustomerApi> GetCustomerByNameAsync(string customerName, CancellationToken cancellationToken)
        {
            if (_fail) throw new HttpRequestException("private upstream detail");
            if (_timeout) throw new TimeoutRejectedException("upstream timeout");
            if (_crash) throw new InvalidOperationException("private exception detail");
            if (_waitForCancellation)
            {
                try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
                finally { _cancellationObserved.TrySetResult(cancellationToken.IsCancellationRequested); }
            }
            if (_delay > TimeSpan.Zero) await Task.Delay(_delay, cancellationToken);
            return _customer;
        }
    }

    private sealed class OrderStub : IOrderService
    {
        private readonly System.Collections.Generic.List<OrderApi> _orders;
        public OrderStub(params OrderApi[] orders) => _orders = new System.Collections.Generic.List<OrderApi>(orders);
        public Task<System.Collections.Generic.List<OrderApi>> GetOrdersByCustomerIdAsync(int customerId, CancellationToken cancellationToken)
            => Task.FromResult(_orders);
    }

    private sealed class ProductStub : IProductService
    {
        private readonly ProductApi _product;
        public ProductStub(ProductApi product = null) => _product = product;
        public Task<ProductApi> GetProductByIdAsync(int productId, CancellationToken cancellationToken)
            => Task.FromResult(_product);
    }
}
