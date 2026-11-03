using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Aggregator.WebApi.HealthChecks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Xunit;

public class GatewayHealthCheckTests
{
    [Fact]
    public async Task Readiness_is_healthy_when_all_services_are_ready()
    {
        using var client = new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)));
        var check = CreateCheck(client);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task Readiness_reports_unavailable_dependency()
    {
        using var client = new HttpClient(new StubHandler(request =>
            new HttpResponseMessage(request.RequestUri.Host == "product" ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK)));
        var check = CreateCheck(client);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Contains("Product", result.Description);
    }

    private static GatewayDependenciesHealthCheck CreateCheck(HttpClient client)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string>
        {
            ["MicroserviceUrls:CustomerService"] = "http://customer",
            ["MicroserviceUrls:OrderService"] = "http://order",
            ["MicroserviceUrls:ProductService"] = "http://product"
        }).Build();
        return new GatewayDependenciesHealthCheck(new StubClientFactory(client), configuration);
    }

    private sealed class StubClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(respond(request));
    }
}
