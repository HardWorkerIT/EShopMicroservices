using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using Customer.Microservice.Contracts;
using Customer.Microservice.Controllers;
using Customer.Microservice.Data;
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
using System.IdentityModel.Tokens.Jwt;
using Xunit;

public sealed class CustomerHttpIntegrationTests
{
    private const string Issuer = "eshop";
    private const string Audience = "eshop-api";
    private const string SigningKey = "integration-test-signing-key-with-at-least-32-bytes";

    [Fact]
    public async Task Customer_http_api_validates_requests_and_enforces_owner_for_update_and_delete()
    {
        await using var fixture = await ApiFixture.CreateAsync();

        using (var invalid = CreateRequest(HttpMethod.Post, "/api/v1/Customer", "1",
                   "{\"name\":\"Invalid\",\"email\":\"not-an-email\"}"))
        using (var invalidResponse = await fixture.Client.SendAsync(invalid))
            Assert.Equal(HttpStatusCode.BadRequest, invalidResponse.StatusCode);

        CustomerResponse created;
        using (var create = CreateRequest(HttpMethod.Post, "/api/v1/Customer", "1",
                   "{\"name\":\"Ada\",\"city\":\"London\",\"email\":\"ada@example.test\"}"))
        using (var response = await fixture.Client.SendAsync(create))
        {
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            created = await response.Content.ReadFromJsonAsync<CustomerResponse>();
            Assert.NotNull(created);
            Assert.Equal("Ada", created.Name);
        }

        using (var foreignUpdate = CreateRequest(HttpMethod.Put, $"/api/v1/Customer/{created.Id}",
                   (created.Id + 1).ToString(), "{\"name\":\"Intruder\"}"))
        using (var response = await fixture.Client.SendAsync(foreignUpdate))
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        using (var ownerUpdate = CreateRequest(HttpMethod.Put, $"/api/v1/Customer/{created.Id}",
                   created.Id.ToString(), "{\"name\":\"Ada Lovelace\",\"city\":\"London\",\"email\":\"ada@example.test\"}"))
        using (var response = await fixture.Client.SendAsync(ownerUpdate))
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        using (var get = await fixture.Client.GetAsync($"/api/v1/Customer/{created.Id}"))
        {
            Assert.Equal(HttpStatusCode.OK, get.StatusCode);
            var updated = await get.Content.ReadFromJsonAsync<CustomerResponse>();
            Assert.NotNull(updated);
            Assert.Equal("Ada Lovelace", updated.Name);
        }

        using (var foreignDelete = CreateRequest(HttpMethod.Delete, $"/api/v1/Customer/{created.Id}",
                   (created.Id + 1).ToString()))
        using (var response = await fixture.Client.SendAsync(foreignDelete))
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        using (var ownerDelete = CreateRequest(HttpMethod.Delete, $"/api/v1/Customer/{created.Id}", created.Id.ToString()))
        using (var response = await fixture.Client.SendAsync(ownerDelete))
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        using var missing = await fixture.Client.GetAsync($"/api/v1/Customer/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, string path, string customerId, string body = null)
    {
        var request = new HttpRequestMessage(method, path);
        if (body is not null)
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");

        var now = DateTime.UtcNow;
        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims: new[] { new Claim("customer_id", customerId) },
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
                ["Jwt:SigningKey"] = SigningKey
            };

            var host = Host.CreateDefaultBuilder().ConfigureWebHostDefaults(web =>
            {
                web.UseUrls("http://127.0.0.1:0");
                web.ConfigureAppConfiguration((_, builder) => builder.AddInMemoryCollection(configuration));
                web.ConfigureServices(services =>
                {
                    services.AddDbContext<ApplicationDbContext>(builder => builder.UseSqlite(connection));
                    services.AddScoped<IApplicationDbContext>(provider => provider.GetRequiredService<ApplicationDbContext>());
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
                    services.AddControllers().AddApplicationPart(typeof(CustomerController).Assembly);
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
