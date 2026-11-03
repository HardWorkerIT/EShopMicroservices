using System.Diagnostics;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace Aggregator.WebApi.Middleware
{
    public class GlobalExceptionHandler
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<GlobalExceptionHandler> _logger;

        public GlobalExceptionHandler(RequestDelegate next, ILogger<GlobalExceptionHandler> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            using var logScope = _logger.BeginScope(new Dictionary<string, object> { ["TraceId"] = Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier });
            try
            {
                await _next(context);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Microservice communication error: {Message}", ex.Message);
                await HandleExceptionAsync(context, ex, HttpStatusCode.ServiceUnavailable, 
                    "Unable to communicate with downstream service.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An unhandled exception occurred: {Message}", ex.Message);
                await HandleExceptionAsync(context, ex, HttpStatusCode.InternalServerError, 
                    "An error occurred while processing your request.");
            }
        }

        private static Task HandleExceptionAsync(HttpContext context, Exception exception, 
            HttpStatusCode statusCode, string message)
        {
            context.Response.ContentType = "application/json";
            context.Response.StatusCode = (int)statusCode;

            var response = new ProblemDetails { Status = context.Response.StatusCode, Title = message, Instance = context.Request.Path };
            response.Extensions["traceId"] = context.TraceIdentifier;
            var jsonResponse = JsonSerializer.Serialize(response);
            return context.Response.WriteAsync(jsonResponse);
        }
    }
}

