using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
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
using OrderContext = Order.Microservice.Data.ApplicationDbContext;
using OrdersController = Customer.Microservice.Controllers.OrderController;
using System.IdentityModel.Tokens.Jwt;
using Xunit;

public sealed class OrderHttpIntegrationTests
{
    private const string Issuer = "eshop";
    private const string Audience = "eshop-api";
    private const string SigningKey = "integration-test-signing-key-with-at-least-32-bytes";

    [Fact]
    public async Task Order_http_api_validates_input_scopes_reads_and_releases_inventory_on_cancel()
    {
        await using var fixture = await ApiFixture.CreateAsync();

        using (var invalid = CreateRequest(HttpMethod.Post, "/api/v1/Order", 42,
                   "{\"productId\":9,\"customerId\":42,\"quantity\":0}"))
        using (var invalidResponse = await fixture.Client.SendAsync(invalid))
            Assert.Equal(HttpStatusCode.BadRequest, invalidResponse.StatusCode);

        OrderResponse created;
        using (var create = CreateRequest(HttpMethod.Post, "/api/v1/Order", 42,
                   "{\"productId\":9,\"customerId\":42,\"quantity\":2,\"date\":\"2026-11-03T00:00:00Z\",\"orderStatus\":2}"))
        using (var response = await fixture.Client.SendAsync(create))
        {
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            created = await response.Content.ReadFromJsonAsync<OrderResponse>();
            Assert.NotNull(created);
            Assert.Equal(42, created.CustomerId);
            Assert.Equal(1, fixture.Inventory.ReserveCalls);
        }

        using (var foreignGet = CreateRequest(HttpMethod.Get, $"/api/v1/Order/{created.Id}", 7))
        using (var response = await fixture.Client.SendAsync(foreignGet))
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        using (var ownerGet = CreateRequest(HttpMethod.Get, $"/api/v1/Order/{created.Id}", 42))
        using (var response = await fixture.Client.SendAsync(ownerGet))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var fetched = await response.Content.ReadFromJsonAsync<OrderResponse>();
            Assert.Equal(created.Id, fetched.Id);
        }

        using (var cancel = CreateRequest(HttpMethod.Put, $"/api/v1/Order/{created.Id}", 42, "{\"orderStatus\":0}"))
        using (var response = await fixture.Client.SendAsync(cancel))
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(1, fixture.Inventory.ReleaseCalls);

        using (var delete = CreateRequest(HttpMethod.Delete, $"/api/v1/Order/{created.Id}", 42))
        using (var response = await fixture.Client.SendAsync(delete))
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(1, fixture.Inventory.ReleaseCalls);

        using var missing = CreateRequest(HttpMethod.Get, $"/api/v1/Order/{created.Id}", 42);
        using var missingResponse = await fixture.Client.SendAsync(missing);
        Assert.Equal(HttpStatusCode.NotFound, missingResponse.StatusCode);
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, string path, long customerId, string body = null)
    {
        var request = new HttpRequestMessage(method, path);
        if (body is not null)
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");

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
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer", new JwtSecurityTokenHandler().WriteToken(token));
        return request;
    }

    private sealed class ApiFixture : IAsyncDisposable
    {
        private readonly IHost _host;
        private readonly SqliteConnection _connection;

        private ApiFixture(IHost host, SqliteConnection connection, HttpClient client, FakeInventoryClient inventory)
        {
            _host = host;
            _connection = connection;
            Client = client;
            Inventory = inventory;
        }

        public HttpClient Client { get; }
        public FakeInventoryClient Inventory { get; }

        public static async Task<ApiFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<OrderContext>().UseSqlite(connection).Options;
            await using (var context = new OrderContext(options))
                await context.Database.EnsureCreatedAsync();

            var configuration = new Dictionary<string, string>
            {
                ["Jwt:Issuer"] = Issuer,
                ["Jwt:Audience"] = Audience,
                ["Jwt:SigningKey"] = SigningKey
            };
            var inventory = new FakeInventoryClient();
            var host = Host.CreateDefaultBuilder().ConfigureWebHostDefaults(web =>
            {
                web.UseUrls("http://127.0.0.1:0");
                web.ConfigureAppConfiguration((_, builder) => builder.AddInMemoryCollection(configuration));
                web.ConfigureServices(services =>
                {
                    services.AddDbContext<OrderContext>(builder => builder.UseSqlite(connection));
                    services.AddSingleton<IProductInventoryClient>(inventory);
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
                    services.AddControllers().AddApplicationPart(typeof(OrdersController).Assembly);
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
            return new ApiFixture(host, connection, client, inventory);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _host.StopAsync();
            _host.Dispose();
            await _connection.DisposeAsync();
        }
    }

    public sealed class FakeInventoryClient : IProductInventoryClient
    {
        public int ReserveCalls { get; private set; }
        public int ReleaseCalls { get; private set; }

        public Task<HttpStatusCode> ReserveAsync(long productId, int quantity, Guid reservationId, CancellationToken cancellationToken)
        {
            ReserveCalls++;
            return Task.FromResult(HttpStatusCode.NoContent);
        }

        public Task ReleaseAsync(long productId, int quantity, Guid reservationId, CancellationToken cancellationToken)
        {
            ReleaseCalls++;
            return Task.CompletedTask;
        }
    }
}
