using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

public sealed class ProblemDetailsMiddlewareTests
{
    [Fact]
    public async Task Every_application_exception_handler_hides_internal_errors_and_returns_trace_id()
    {
        var handlers = new Func<DefaultHttpContext, Task>[]
        {
            context => new Customer.Microservice.Middleware.GlobalExceptionHandler(
                _ => Task.FromException(new InvalidOperationException("customer secret detail")),
                NullLogger<Customer.Microservice.Middleware.GlobalExceptionHandler>.Instance).InvokeAsync(context),
            context => new Order.Microservice.Middleware.GlobalExceptionHandler(
                _ => Task.FromException(new InvalidOperationException("order secret detail")),
                NullLogger<Order.Microservice.Middleware.GlobalExceptionHandler>.Instance).InvokeAsync(context),
            context => new Product.Microservice.Middleware.GlobalExceptionHandler(
                _ => Task.FromException(new InvalidOperationException("product secret detail")),
                NullLogger<Product.Microservice.Middleware.GlobalExceptionHandler>.Instance).InvokeAsync(context),
            context => new Aggregator.WebApi.Middleware.GlobalExceptionHandler(
                _ => Task.FromException(new InvalidOperationException("gateway secret detail")),
                NullLogger<Aggregator.WebApi.Middleware.GlobalExceptionHandler>.Instance).InvokeAsync(context)
        };

        foreach (var handler in handlers)
        {
            var context = CreateContext();
            await handler(context);
            AssertProblemResponse(context, HttpStatusCode.InternalServerError, "secret detail");
        }
    }

    [Fact]
    public async Task Gateway_maps_downstream_transport_errors_to_sanitized_service_unavailable_problem()
    {
        var context = CreateContext();
        var middleware = new Aggregator.WebApi.Middleware.GlobalExceptionHandler(
            _ => Task.FromException(new HttpRequestException("downstream host and credentials")),
            NullLogger<Aggregator.WebApi.Middleware.GlobalExceptionHandler>.Instance);

        await middleware.InvokeAsync(context);

        AssertProblemResponse(context, HttpStatusCode.ServiceUnavailable, "downstream host and credentials");
        using var document = JsonDocument.Parse(((MemoryStream)context.Response.Body).ToArray());
        Assert.Equal("Unable to communicate with downstream service.", document.RootElement.GetProperty("title").GetString());
    }

    private static DefaultHttpContext CreateContext()
    {
        var context = new DefaultHttpContext();
        context.TraceIdentifier = "test-trace-id";
        context.Request.Path = "/api/secret";
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static void AssertProblemResponse(DefaultHttpContext context, HttpStatusCode expectedStatus, string forbiddenText)
    {
        Assert.Equal((int)expectedStatus, context.Response.StatusCode);
        Assert.Equal("application/json", context.Response.ContentType);
        using var document = JsonDocument.Parse(((MemoryStream)context.Response.Body).ToArray());
        var root = document.RootElement;
        Assert.Equal((int)expectedStatus, root.GetProperty("status").GetInt32());
        Assert.Equal("/api/secret", root.GetProperty("instance").GetString());
        Assert.Equal("test-trace-id", root.GetProperty("traceId").GetString());
        Assert.DoesNotContain(forbiddenText, root.GetProperty("title").GetString());
        Assert.DoesNotContain(forbiddenText, root.GetRawText());
    }
}
