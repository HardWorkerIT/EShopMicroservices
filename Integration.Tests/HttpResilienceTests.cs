using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Aggregator.WebApi.Customer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly.Timeout;
using Polly.CircuitBreaker;
using Polly.RateLimiting;
using Xunit;

public class HttpResilienceTests
{
    [Fact]
    public async Task Customer_service_retries_a_transient_503()
    {
        var handler = new RetryHandler();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpClient(Options.DefaultName)
            .ConfigurePrimaryHttpMessageHandler(() => handler)
            .AddStandardResilienceHandler(options =>
            {
                options.Retry.MaxRetryAttempts = 1;
                options.Retry.Delay = TimeSpan.FromMilliseconds(1);
                options.Retry.BackoffType = Polly.DelayBackoffType.Constant;
                options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(2);
                options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(4);
            });
        using var provider = services.BuildServiceProvider();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new[]
        {
            new System.Collections.Generic.KeyValuePair<string, string>("MicroserviceUrls:CustomerService", "http://customer")
        }).Build();
        var customerService = new CustomerService(provider.GetRequiredService<IHttpClientFactory>(), configuration);

        var customer = await customerService.GetCustomerByNameAsync("Guest", CancellationToken.None);

        Assert.Equal(42, customer.Id);
        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task Standard_resilience_handler_times_out_a_hanging_downstream()
    {
        var services = new ServiceCollection();
        services.AddHttpClient("timeout-test")
            .ConfigurePrimaryHttpMessageHandler(() => new HangingHandler())
            .AddStandardResilienceHandler(options =>
            {
                options.Retry.MaxRetryAttempts = 1;
                options.AttemptTimeout.Timeout = TimeSpan.FromMilliseconds(100);
                options.TotalRequestTimeout.Timeout = TimeSpan.FromMilliseconds(500);
            });
        using var provider = services.BuildServiceProvider();
        using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("timeout-test");

        await Assert.ThrowsAsync<TimeoutRejectedException>(() => client.GetAsync("http://downstream"));
    }

    [Fact]
    public async Task Standard_resilience_handler_opens_the_circuit_after_repeated_failures()
    {
        var handler = new AlwaysUnavailableHandler();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpClient("breaker-test")
            .ConfigurePrimaryHttpMessageHandler(() => handler)
            .AddStandardResilienceHandler(options =>
            {
                options.Retry.MaxRetryAttempts = 1;
                options.Retry.Delay = TimeSpan.FromMilliseconds(1);
                options.Retry.BackoffType = Polly.DelayBackoffType.Constant;
                options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(2);
                options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(5);
                options.CircuitBreaker.FailureRatio = 0.5;
                options.CircuitBreaker.MinimumThroughput = 2;
                options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
                options.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(30);
            });
        using var provider = services.BuildServiceProvider();
        using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("breaker-test");
        var circuitOpened = false;

        for (var attempt = 0; attempt < 3 && !circuitOpened; attempt++)
        {
            try
            {
                using var response = await client.GetAsync("http://downstream");
            }
            catch (BrokenCircuitException)
            {
                circuitOpened = true;
            }
        }

        Assert.True(handler.RequestCount >= 2);
        Assert.True(circuitOpened, "Repeated 503 responses should open the circuit and reject a subsequent request.");
    }

    [Fact]
    public async Task Standard_resilience_handler_rejects_requests_over_the_concurrency_limit()
    {
        var handler = new BlockingHandler();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpClient("limiter-test")
            .ConfigurePrimaryHttpMessageHandler(() => handler)
            .AddStandardResilienceHandler(options =>
            {
                options.Retry.MaxRetryAttempts = 1;
                options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(5);
                options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(10);
                options.RateLimiter.DefaultRateLimiterOptions.PermitLimit = 1;
                options.RateLimiter.DefaultRateLimiterOptions.QueueLimit = 0;
            });
        using var provider = services.BuildServiceProvider();
        using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("limiter-test");
        var firstRequest = client.GetAsync("http://downstream");
        await handler.RequestStarted.WaitAsync(TimeSpan.FromSeconds(2));

        try
        {
            await Assert.ThrowsAsync<RateLimiterRejectedException>(() => client.GetAsync("http://downstream"));
        }
        finally
        {
            handler.ReleaseRequest();
        }

        using var firstResponse = await firstRequest;
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
    }

    private sealed class RetryHandler : HttpMessageHandler
    {
        private int _requestCount;
        public int RequestCount => _requestCount;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _requestCount) == 1)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"id\":42,\"name\":\"Guest\"}")
            });
        }
    }

    private sealed class HangingHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private sealed class AlwaysUnavailableHandler : HttpMessageHandler
    {
        private int _requestCount;
        public int RequestCount => _requestCount;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _requestCount);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        }
    }

    private sealed class BlockingHandler : HttpMessageHandler
    {
        private readonly TaskCompletionSource _requestStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _releaseRequest = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task RequestStarted => _requestStarted.Task;

        public void ReleaseRequest() => _releaseRequest.TrySetResult();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _requestStarted.TrySetResult();
            await _releaseRequest.Task.WaitAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}
