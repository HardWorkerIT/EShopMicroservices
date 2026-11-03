using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using Customer.Microservice.Controllers;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using Order.Microservice.Clients;
using Order.Microservice.Contracts;
using Product.Microservice.Contracts;
using Product.Microservice.Services;
using ProductController = Product.Microservice.Controllers.ProductController;
using ProductContext = Product.Microservice.Data.ApplicationDbContext;
using OrderContext = Order.Microservice.Data.ApplicationDbContext;
using Xunit;

public sealed class OrderProductHttpIntegrationTests
{
    private const string Issuer = "eshop";
    private const string Audience = "eshop-api";
    private const string SigningKey = "integration-test-signing-key-with-at-least-32-bytes";
    private const string ServiceKey = "integration-service-key-with-at-least-32-bytes";

    [Fact]
    public async Task Order_service_calls_product_http_api_and_compensates_failed_and_cancelled_orders()
    {
        await using var fixture = await ApiFixture.CreateAsync();
        var token = CreateCustomerToken(42);

        using (var overstock = CreateOrderRequest(fixture.ProductId, quantity: 6, token))
        using (var response = await fixture.Client.SendAsync(overstock))
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(5, await fixture.GetInventoryCountAsync());

        OrderResponse created;
        using (var create = CreateOrderRequest(fixture.ProductId, quantity: 2, token))
        using (var response = await fixture.Client.SendAsync(create))
        {
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            created = await response.Content.ReadFromJsonAsync<OrderResponse>();
            Assert.NotNull(created);
            Assert.Equal(42, created.CustomerId);
        }
        Assert.Equal(3, await fixture.GetInventoryCountAsync());

        using (var cancel = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/Order/{created.Id}")
               { Content = new StringContent("{\"orderStatus\":0}", Encoding.UTF8, "application/json") })
        {
            cancel.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            using var response = await fixture.Client.SendAsync(cancel);
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        Assert.Equal(5, await fixture.GetInventoryCountAsync());
        await using var orderContext = fixture.CreateOrderContext();
        var persistedOrder = await orderContext.Orders.SingleAsync();
        Assert.Equal((int)Order.Microservice.Enums.OrderStatus.Cancelled, persistedOrder.OrderStatus);
    }

    private static HttpRequestMessage CreateOrderRequest(long productId, int quantity, string token)
    {
        var body = System.Text.Json.JsonSerializer.Serialize(new
        {
            ProductId = productId,
            CustomerId = 42,
            Quantity = quantity,
            Date = DateTime.UtcNow,
            OrderStatus = (int)Order.Microservice.Enums.OrderStatus.InProgress
        });
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/Order")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private static string CreateCustomerToken(long customerId)
    {
        var now = DateTime.UtcNow;
        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims: new[] { new Claim("customer_id", customerId.ToString()) },
            notBefore: now.AddMinutes(-1),
            expires: now.AddMinutes(5),
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)),
                SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private sealed class ApiFixture : IAsyncDisposable
    {
        private readonly IHost _host;
        private readonly SqliteConnection _productConnection;
        private readonly SqliteConnection _orderConnection;

        private ApiFixture(IHost host, SqliteConnection productConnection, SqliteConnection orderConnection, HttpClient client, int productId)
        {
            _host = host;
            _productConnection = productConnection;
            _orderConnection = orderConnection;
            Client = client;
            ProductId = productId;
        }

        public HttpClient Client { get; }
        public int ProductId { get; }

        public static async Task<ApiFixture> CreateAsync()
        {
            var productConnection = new SqliteConnection("Data Source=:memory:");
            var orderConnection = new SqliteConnection("Data Source=:memory:");
            await productConnection.OpenAsync();
            await orderConnection.OpenAsync();

            var productOptions = new DbContextOptionsBuilder<ProductContext>().UseSqlite(productConnection).Options;
            int productId;
            await using (var productContext = new ProductContext(productOptions))
            {
                await productContext.Database.EnsureCreatedAsync();
                var product = new Product.Microservice.Entities.Product
                {
                    Name = "HTTP integration product",
                    Rate = 25,
                    InventoryCount = 5
                };
                productContext.Products.Add(product);
                await productContext.SaveChangesAsync();
                productId = product.Id;
            }
            var orderOptions = new DbContextOptionsBuilder<OrderContext>().UseSqlite(orderConnection).Options;
            await using (var orderContext = new OrderContext(orderOptions))
                await orderContext.Database.EnsureCreatedAsync();

            using var portProbe = new TcpListener(System.Net.IPAddress.Loopback, 0);
            portProbe.Start();
            var port = ((System.Net.IPEndPoint)portProbe.LocalEndpoint).Port;
            portProbe.Stop();
            var baseAddress = $"http://127.0.0.1:{port}";
            var configuration = new Dictionary<string, string>
            {
                ["Jwt:Issuer"] = Issuer,
                ["Jwt:Audience"] = Audience,
                ["Jwt:SigningKey"] = SigningKey,
                ["ServiceAuth:Key"] = ServiceKey,
                ["MicroserviceUrls:ProductService"] = baseAddress
            };

            var host = Host.CreateDefaultBuilder().ConfigureWebHostDefaults(web =>
            {
                web.UseUrls(baseAddress);
                web.ConfigureAppConfiguration((_, builder) => builder.AddInMemoryCollection(configuration));
                web.ConfigureServices(services =>
                {
                    services.AddDbContext<ProductContext>(builder => builder.UseSqlite(productConnection));
                    services.AddScoped<Product.Microservice.Data.IApplicationDbContext>(provider => provider.GetRequiredService<ProductContext>());
                    services.AddScoped<IInventoryService, InventoryService>();
                    services.AddDbContext<OrderContext>(builder => builder.UseSqlite(orderConnection));
                    services.AddHttpClient<IProductInventoryClient, ProductInventoryClient>();
                    services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
                    {
                        options.TokenValidationParameters = new TokenValidationParameters
                        {
                            ValidateIssuer = true,
                            ValidIssuer = Issuer,
                            ValidateAudience = true,
                            ValidAudience = Audience,
                            ValidateLifetime = true,
                            ValidateIssuerSigningKey = true,
                            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey))
                        };
                    });
                    services.AddAuthorization();
                    services.AddControllers()
                        .AddApplicationPart(typeof(ProductController).Assembly)
                        .AddApplicationPart(typeof(OrderController).Assembly);
                });
                web.Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints => endpoints.MapControllers());
                });
            }).Build();

            await host.StartAsync();
            var addresses = host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>();
            var client = new HttpClient { BaseAddress = new Uri(System.Linq.Enumerable.Single(addresses.Addresses)) };
            return new ApiFixture(host, productConnection, orderConnection, client, productId);
        }

        public async Task<int> GetInventoryCountAsync()
        {
            using var response = await Client.GetAsync($"/api/v1/Product/{ProductId}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var product = await response.Content.ReadFromJsonAsync<ProductResponse>();
            Assert.NotNull(product);
            return product.InventoryCount;
        }

        public OrderContext CreateOrderContext()
            => new(new DbContextOptionsBuilder<OrderContext>().UseSqlite(_orderConnection).Options);

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _host.StopAsync();
            _host.Dispose();
            await _productConnection.DisposeAsync();
            await _orderConnection.DisposeAsync();
        }
    }
}
