using System.Net;
using System.Net.Http.Headers;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

using Modulix.ReverseProxy;
using Modulix.ReverseProxy.Models;
using Modulix.ReverseProxy.Services;

namespace Modulix.Tests.Services;

/// <summary>
/// End-to-end HTTP request forwarding tests for <see cref="ReverseProxyService"/> and <see cref="ReverseProxyRegistration"/>.
/// </summary>
public partial class ReverseProxyServiceTests
{
    [Fact]
    public async Task MapModulixReverseProxy_ForwardsActiveEndpointsAndAuthorizationHeaders_Returns404ForUnknownRoutes()
    {
        var containerBuilder = WebApplication.CreateBuilder();
        containerBuilder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var containerApp = containerBuilder.Build();

        string? receivedPath = null;
        string? receivedAuthHeader = null;

        containerApp.MapGet("/api/reports", (HttpContext context) =>
        {
            receivedPath = context.Request.Path.Value;
            receivedAuthHeader = context.Request.Headers.Authorization.ToString();
            return Results.Ok("report-payload");
        });

        await containerApp.StartAsync();
        var containerAddress = containerApp.Services
            .GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses.Single();
        var containerPort = new Uri(containerAddress).Port;

        var gatewayBuilder = WebApplication.CreateBuilder();
        gatewayBuilder.WebHost.UseUrls("http://127.0.0.1:0");
        gatewayBuilder.Services
            .AddAuthorization()
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer();
        gatewayBuilder.Services.AddReverseProxyServices();

        await using var gatewayApp = gatewayBuilder.Build();
        gatewayApp.UseAuthentication();
        gatewayApp.UseAuthorization();
        gatewayApp.MapModulixReverseProxy();
        await gatewayApp.StartAsync();

        var gatewayAddress = gatewayApp.Services
            .GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses.Single();

        var proxyService = gatewayApp.Services.GetRequiredService<IReverseProxyService>();
        var moduleId = Guid.NewGuid();

        using var httpClient = new HttpClient(new SocketsHttpHandler { UseProxy = false }) { BaseAddress = new Uri(gatewayAddress) };
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "container-specific-token");

        var beforeRegistrationResponse = await httpClient.GetAsync("/analytics/api/reports");
        Assert.Equal(HttpStatusCode.NotFound, beforeRegistrationResponse.StatusCode);

        proxyService.UpsertModule(new ProxyModuleDefinition
        {
            ModuleId = moduleId,
            BaseUrlPath = "analytics",
            ContainerPort = containerPort,
            Endpoints =
            [
                new ProxyEndpointDefinition { HttpMethod = "GET", EndpointPath = "/api/reports" }
            ]
        });

        var activeEndpointResponse = await httpClient.GetAsync("/analytics/api/reports");
        Assert.Equal(HttpStatusCode.OK, activeEndpointResponse.StatusCode);
        Assert.Equal("\"report-payload\"", await activeEndpointResponse.Content.ReadAsStringAsync());
        Assert.Equal("/api/reports", receivedPath);
        Assert.Equal("Bearer " + "container-specific-token", receivedAuthHeader);

        receivedPath = null;
        var unknownEndpointResponse = await httpClient.GetAsync("/analytics/api/unconfirmed");
        Assert.Equal(HttpStatusCode.NotFound, unknownEndpointResponse.StatusCode);
        Assert.Null(receivedPath);

        var wrongMethodResponse = await httpClient.PostAsync("/analytics/api/reports", content: null);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, wrongMethodResponse.StatusCode);
        Assert.Null(receivedPath);

        proxyService.RemoveModule(moduleId);
        var afterRemovalResponse = await httpClient.GetAsync("/analytics/api/reports");
        Assert.Equal(HttpStatusCode.NotFound, afterRemovalResponse.StatusCode);

        await gatewayApp.StopAsync();
        await containerApp.StopAsync();
    }
}
