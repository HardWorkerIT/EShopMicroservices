using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
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
using Product.Microservice.Controllers;
using Product.Microservice.Contracts;
using Product.Microservice.Data;
using Product.Microservice.Services;
using Xunit;

public class ProductAuthorizationTests
{
    private const string Issuer = "eshop";
    private const string Audience = "eshop-api";
    private const string SigningKey = "integration-test-signing-key-with-at-least-32-bytes";
    private const string ServiceKey = "integration-service-key-with-at-least-32-bytes";

    [Fact]
    public async Task Admin_token_can_create_a_product()
    {
        await using var fixture = await ApiFixture.CreateAsync();
        using var request = CreateProductRequest("Admin", SigningKey);

        using var response = await fixture.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Non_admin_token_cannot_create_a_product()
    {
        await using var fixture = await ApiFixture.CreateAsync();
        using var request = CreateProductRequest("Customer", SigningKey);

        using var response = await fixture.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Product_write_rejects_a_token_with_an_invalid_signature()
    {
        await using var fixture = await ApiFixture.CreateAsync();
        using var request = CreateProductRequest("Admin", "different-integration-signing-key-with-32-bytes");

        using var response = await fixture.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Product_write_validates_the_request_contract()
    {
        await using var fixture = await ApiFixture.CreateAsync();
        using var request = CreateProductRequest(
            "Admin",
            SigningKey,
            "{\"name\":\"\",\"rate\":-1,\"inventoryCount\":5}");

        using var response = await fixture.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Admin_can_create_read_update_and_delete_a_product_over_http()
    {
        await using var fixture = await ApiFixture.CreateAsync();

        using (var create = CreateAdminRequest(HttpMethod.Post, "/api/v1/Product",
                   "{\"name\":\"Lifecycle product\",\"rate\":10,\"inventoryCount\":5}"))
        using (var response = await fixture.Client.SendAsync(create))
        {
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var created = await response.Content.ReadFromJsonAsync<ProductResponse>();
            Assert.NotNull(created);
            Assert.True(created.Id > 0);
            Assert.Equal("Lifecycle product", created.Name);

            using var get = await fixture.Client.GetAsync($"/api/v1/Product/{created.Id}");
            Assert.Equal(HttpStatusCode.OK, get.StatusCode);
            var fetched = await get.Content.ReadFromJsonAsync<ProductResponse>();
            Assert.Equal(created, fetched);

            using var update = CreateAdminRequest(HttpMethod.Put, $"/api/v1/Product/{created.Id}",
                "{\"name\":\"Updated product\",\"rate\":25,\"inventoryCount\":8}");
            using var updateResponse = await fixture.Client.SendAsync(update);
            Assert.Equal(HttpStatusCode.NoContent, updateResponse.StatusCode);

            using var updatedResponse = await fixture.Client.GetAsync($"/api/v1/Product/{created.Id}");
            var updated = await updatedResponse.Content.ReadFromJsonAsync<ProductResponse>();
            Assert.Equal(HttpStatusCode.OK, updatedResponse.StatusCode);
            Assert.NotNull(updated);
            Assert.Equal("Updated product", updated.Name);
            Assert.Equal(25, updated.Rate);
            Assert.Equal(8, updated.InventoryCount);

            using var delete = CreateAdminRequest(HttpMethod.Delete, $"/api/v1/Product/{created.Id}");
            using var deleteResponse = await fixture.Client.SendAsync(delete);
            Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

            using var missing = await fixture.Client.GetAsync($"/api/v1/Product/{created.Id}");
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        }
    }

    private static HttpRequestMessage CreateAdminRequest(HttpMethod method, string path, string body = null)
    {
        var request = new HttpRequestMessage(method, path);
        if (body is not null)
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");

        var now = DateTime.UtcNow;
        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims: new[] { new Claim(ClaimTypes.Role, "Admin") },
            notBefore: now.AddMinutes(-1),
            expires: now.AddMinutes(5),
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)),
                SecurityAlgorithms.HmacSha256));
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer", new JwtSecurityTokenHandler().WriteToken(token));
        return request;
    }

    [Fact]
    public async Task Inventory_release_accepts_the_service_key_used_by_order_service()
    {
        await using var fixture = await ApiFixture.CreateAsync();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/Product/404/release")
        {
            Content = new StringContent("{\"quantity\":1,\"reservationId\":\"" + Guid.NewGuid() + "\"}", Encoding.UTF8, "application/json")
        };
        request.Headers.Add("X-Service-Key", ServiceKey);

        using var response = await fixture.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Inventory_release_rejects_an_invalid_service_key()
    {
        await using var fixture = await ApiFixture.CreateAsync();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/Product/404/release")
        {
            Content = new StringContent("{\"quantity\":1}", Encoding.UTF8, "application/json")
        };
        request.Headers.Add("X-Service-Key", "incorrect-key");

        using var response = await fixture.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Inventory_endpoint_rejects_a_quantity_outside_the_order_service_contract()
    {
        await using var fixture = await ApiFixture.CreateAsync();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/Product/404/reserve")
        {
            Content = new StringContent(
                "{\"quantity\":10001,\"reservationId\":\"" + Guid.NewGuid() + "\"}",
                Encoding.UTF8,
                "application/json")
        };
        request.Headers.Add("X-Service-Key", ServiceKey);

        using var response = await fixture.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static HttpRequestMessage CreateProductRequest(string role, string key, string body = "{\"name\":\"Test product\",\"rate\":10,\"inventoryCount\":5}")
    {
        var now = DateTime.UtcNow;
        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims: new[] { new Claim(ClaimTypes.Role, role) },
            notBefore: now.AddMinutes(-1),
            expires: now.AddMinutes(5),
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
                SecurityAlgorithms.HmacSha256));

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/Product")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer", new JwtSecurityTokenHandler().WriteToken(token));
        return request;
    }

    private sealed class ApiFixture : IAsyncDisposable
    {
        private readonly IHost _host;
        private readonly SqliteConnection _connection;

        private ApiFixture(IHost host, SqliteConnection connection, HttpClient client)
        {
            _host = host;
            _connection = connection;
            Client = client;
        }

        public HttpClient Client { get; }

        public static async Task<ApiFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
            await using (var context = new ApplicationDbContext(options))
                await context.Database.EnsureCreatedAsync();

            var configuration = new Dictionary<string, string>
            {
                ["Jwt:Issuer"] = Issuer,
                ["Jwt:Audience"] = Audience,
                ["Jwt:SigningKey"] = SigningKey,
                ["ServiceAuth:Key"] = ServiceKey
            };

            var host = Host.CreateDefaultBuilder().ConfigureWebHostDefaults(web =>
            {
                web.UseUrls("http://127.0.0.1:0");
                web.ConfigureAppConfiguration((_, builder) => builder.AddInMemoryCollection(configuration));
                web.ConfigureServices(services =>
                {
                    services.AddDbContext<ApplicationDbContext>(builder => builder.UseSqlite(connection));
                    services.AddScoped<IApplicationDbContext>(provider => provider.GetRequiredService<ApplicationDbContext>());
                    services.AddScoped<IInventoryService, InventoryService>();
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
                    services.AddControllers().AddApplicationPart(typeof(ProductController).Assembly);
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
            return new ApiFixture(host, connection, client);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _host.StopAsync();
            _host.Dispose();
            await _connection.DisposeAsync();
        }
    }
}
