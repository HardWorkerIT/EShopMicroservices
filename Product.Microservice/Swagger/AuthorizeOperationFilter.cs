using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Product.Microservice.Swagger;

[AttributeUsage(AttributeTargets.Method)]
public sealed class ServiceKeyRequiredAttribute : Attribute { }

public sealed class AuthorizeOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var attributes = context.MethodInfo.GetCustomAttributes(true)
            .Concat(context.MethodInfo.DeclaringType?.GetCustomAttributes(true) ?? Array.Empty<object>())
            .ToArray();

        if (attributes.OfType<ServiceKeyRequiredAttribute>().Any())
        {
            Require(operation, "ServiceKey");
            return;
        }

        if (attributes.OfType<IAllowAnonymous>().Any() || !attributes.OfType<IAuthorizeData>().Any()) return;
        Require(operation, "Bearer");
    }

    private static void Require(OpenApiOperation operation, string scheme)
    {
        operation.Security ??= new List<OpenApiSecurityRequirement>();
        operation.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = scheme }
            }] = new List<string>()
        });
    }
}
