using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Aggregator.WebApi.Customer;
using Microsoft.Extensions.Configuration;
using OpenTelemetry;
using OpenTelemetry.Trace;
using Xunit;

public class TracingPropagationTests
{
    [Fact]
    public async Task HttpClient_propagates_the_current_w3c_trace_context()
    {
        using var tracerProvider = Sdk.CreateTracerProviderBuilder()
            .AddHttpClientInstrumentation()
            .Build();
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var captureTask = Task.Run(async () =>
        {
            using var connection = await listener.AcceptTcpClientAsync();
            var stream = connection.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, leaveOpen: true);
            string traceparent = null;
            string line;
            while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync()))
            {
                if (line.StartsWith("traceparent:", StringComparison.OrdinalIgnoreCase))
                    traceparent = line[(line.IndexOf(':') + 1)..].Trim();
            }

            const string body = "{\"id\":42,\"name\":\"Guest\"}";
            var response = $"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {Encoding.UTF8.GetByteCount(body)}\r\nConnection: close\r\n\r\n{body}";
            await stream.WriteAsync(Encoding.ASCII.GetBytes(response));
            await stream.FlushAsync();
            return traceparent;
        });

        using var client = new HttpClient();
        var customerService = new CustomerService(
            new StubClientFactory(client),
            new ConfigurationBuilder().AddInMemoryCollection(new[]
            {
                new System.Collections.Generic.KeyValuePair<string, string>("MicroserviceUrls:CustomerService", $"http://127.0.0.1:{port}")
            }).Build());
        using var parent = new Activity("gateway-request").SetIdFormat(ActivityIdFormat.W3C).Start();

        var customer = await customerService.GetCustomerByNameAsync("Guest", CancellationToken.None);
        var traceparent = await captureTask.WaitAsync(TimeSpan.FromSeconds(5));
        listener.Stop();

        Assert.Equal(42, customer.Id);
        Assert.True(ActivityContext.TryParse(traceparent, null, out var propagatedContext));
        Assert.Equal(parent.TraceId, propagatedContext.TraceId);
    }

    private sealed class StubClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }
}
