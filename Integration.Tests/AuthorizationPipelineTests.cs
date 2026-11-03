using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Customer.Microservice.Controllers;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OrderController = Customer.Microservice.Controllers.OrderController;
using Xunit;

public class AuthorizationPipelineTests
{
    [Fact]
    public async Task Customer_and_order_write_routes_require_authentication()
    {
        using var host = await StartHost();
        using var client = CreateClient(host);

        using var orderResponse = await client.GetAsync("/api/v1/Order");
        using var customerResponse = await client.PostAsync(
            "/api/v1/Customer",
            new StringContent("{\"name\":\"Example\",\"email\":\"example@example.test\"}", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.Unauthorized, orderResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, customerResponse.StatusCode);
    }

    private static async Task<IHost> StartHost()
    {
        var host = Host.CreateDefaultBuilder().ConfigureWebHostDefaults(web =>
        {
            web.UseUrls("http://127.0.0.1:0");
            web.ConfigureServices(services =>
            {
                services.AddAuthentication("test")
                    .AddScheme<AuthenticationSchemeOptions, NoAuthenticationHandler>("test", _ => { });
                services.AddAuthorization();
                services.AddControllers()
                    .AddApplicationPart(typeof(OrderController).Assembly)
                    .AddApplicationPart(typeof(CustomerController).Assembly);
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
        return host;
    }

    private static HttpClient CreateClient(IHost host)
    {
        var addresses = host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>();
        return new HttpClient { BaseAddress = new Uri(System.Linq.Enumerable.Single(addresses.Addresses)) };
    }

    private sealed class NoAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
            => Task.FromResult(AuthenticateResult.NoResult());
    }
}
