using System.Net;
using System.Net.Http.Headers;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using Yarp.ReverseProxy.Configuration;

using Modulix.ReverseProxy;
using Modulix.ReverseProxy.Models;
using Modulix.ReverseProxy.Services;

namespace Modulix.Tests.Services;

/// <summary>
/// Unit tests for <see cref="ReverseProxyService"/> and <see cref="ReverseProxyRegistration"/>.
/// </summary>
[Trait("Category", "Services")]
[Trait("SubCategory", "ReverseProxyService")]
public class ReverseProxyServiceTests
{
    private readonly ReverseProxyService _sut = new(NullLogger<ReverseProxyService>.Instance);

    #region Initial State & Registration Tests

    [Fact]
    public void GetConfig_InitialState_ReturnsEmptyRoutesAndClusters()
    {
        var config = _sut.GetConfig();

        Assert.Empty(config.Routes);
        Assert.Empty(config.Clusters);
        Assert.False(config.ChangeToken.HasChanged);
    }

    [Fact]
    public void AddReverseProxyServices_RegistersSharedSingletonForServiceAndConfigProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddReverseProxyServices();

        using var provider = services.BuildServiceProvider();
        var proxyService = provider.GetRequiredService<IReverseProxyService>();
        var configProvider = provider.GetRequiredService<IProxyConfigProvider>();

        Assert.Same(proxyService, configProvider);
    }

    #endregion

    #region Route & Cluster Generation Tests

    [Fact]
    public void UpsertModule_ValidModule_GeneratesClusterRoutesPathRemovePrefixTransformAndSignalsChange()
    {
        var initialConfig = _sut.GetConfig();
        var moduleId = Guid.NewGuid();

        _sut.UpsertModule(new ProxyModuleDefinition
        {
            ModuleId = moduleId,
            BaseUrlPath = "/analytics/",
            ContainerPort = 8085,
            Endpoints =
            [
                new ProxyEndpointDefinition { HttpMethod = "get", EndpointPath = "/api/reports" },
                new ProxyEndpointDefinition { HttpMethod = "POST", EndpointPath = "/" }
            ]
        });

        Assert.True(initialConfig.ChangeToken.HasChanged);

        var updatedConfig = _sut.GetConfig();
        Assert.False(updatedConfig.ChangeToken.HasChanged);

        var cluster = Assert.Single(updatedConfig.Clusters);
        Assert.Equal($"module-cluster-{moduleId:N}", cluster.ClusterId);
        Assert.NotNull(cluster.Destinations);
        var destination = Assert.Single(cluster.Destinations);
        Assert.Equal("container", destination.Key);
        Assert.Equal("http://127.0.0.1:8085", destination.Value.Address);

        Assert.Equal(2, updatedConfig.Routes.Count);

        var reportsRoute = updatedConfig.Routes[0];
        Assert.Equal($"module-cluster-{moduleId:N}", reportsRoute.ClusterId);
        Assert.Equal("Anonymous", reportsRoute.AuthorizationPolicy);
        Assert.Equal("/analytics/api/reports", reportsRoute.Match.Path);
        Assert.Equal(["GET"], reportsRoute.Match.Methods);
        Assert.NotNull(reportsRoute.Transforms);
        Assert.Contains(reportsRoute.Transforms, transform =>
            transform.TryGetValue("PathRemovePrefix", out var prefix) && prefix == "/analytics");

        var rootRoute = updatedConfig.Routes[1];
        Assert.Equal("/analytics", rootRoute.Match.Path);
        Assert.Equal(["POST"], rootRoute.Match.Methods);
        Assert.NotNull(rootRoute.Transforms);
        Assert.Contains(rootRoute.Transforms, transform =>
            transform.TryGetValue("PathRemovePrefix", out var prefix) && prefix == "/analytics");
    }

    [Fact]
    public void UpsertModule_ExistingModule_ReplacesClusterAndRoutes()
    {
        var moduleId = Guid.NewGuid();
        _sut.UpsertModule(new ProxyModuleDefinition
        {
            ModuleId = moduleId,
            BaseUrlPath = "billing",
            ContainerPort = 8080,
            Endpoints = [new ProxyEndpointDefinition { HttpMethod = "GET", EndpointPath = "/v1" }]
        });

        var firstUpdateConfig = _sut.GetConfig();

        _sut.UpsertModule(new ProxyModuleDefinition
        {
            ModuleId = moduleId,
            BaseUrlPath = "billing/v2",
            ContainerPort = 9090,
            Endpoints = [new ProxyEndpointDefinition { HttpMethod = "PUT", EndpointPath = "/invoices" }]
        });

        Assert.True(firstUpdateConfig.ChangeToken.HasChanged);

        var currentConfig = _sut.GetConfig();
        var cluster = Assert.Single(currentConfig.Clusters);
        Assert.Equal("http://127.0.0.1:9090", cluster.Destinations!["container"].Address);

        var route = Assert.Single(currentConfig.Routes);
        Assert.Equal("/billing/v2/invoices", route.Match.Path);
        Assert.Equal(["PUT"], route.Match.Methods);
        Assert.Contains(route.Transforms!, transform =>
            transform.TryGetValue("PathRemovePrefix", out var prefix) && prefix == "/billing/v2");
    }

    [Fact]
    public void UpdateModules_ReplacesAllExistingModules()
    {
        var firstModuleId = Guid.NewGuid();
        var secondModuleId = Guid.NewGuid();

        _sut.UpsertModule(new ProxyModuleDefinition
        {
            ModuleId = firstModuleId,
            BaseUrlPath = "old-module",
            ContainerPort = 8001,
            Endpoints = [new ProxyEndpointDefinition { HttpMethod = "GET", EndpointPath = "/status" }]
        });

        var beforeUpdate = _sut.GetConfig();

        _sut.UpdateModules(
        [
            new ProxyModuleDefinition
            {
                ModuleId = secondModuleId,
                BaseUrlPath = "new-module",
                ContainerPort = 8002,
                Endpoints = [new ProxyEndpointDefinition { HttpMethod = "DELETE", EndpointPath = "/items/{id}" }]
            }
        ]);

        Assert.True(beforeUpdate.ChangeToken.HasChanged);

        var currentConfig = _sut.GetConfig();
        var cluster = Assert.Single(currentConfig.Clusters);
        Assert.Equal($"module-cluster-{secondModuleId:N}", cluster.ClusterId);
        var route = Assert.Single(currentConfig.Routes);
        Assert.Equal("/new-module/items/{id}", route.Match.Path);
        Assert.Equal(["DELETE"], route.Match.Methods);
    }

    [Fact]
    public void RemoveModule_RemovesRegisteredModuleAndIgnoresUnknownModule()
    {
        var moduleId = Guid.NewGuid();
        _sut.UpsertModule(new ProxyModuleDefinition
        {
            ModuleId = moduleId,
            BaseUrlPath = "inventory",
            ContainerPort = 8088,
            Endpoints = [new ProxyEndpointDefinition { HttpMethod = "GET", EndpointPath = "/stock" }]
        });

        var configBeforeUnknownRemoval = _sut.GetConfig();
        _sut.RemoveModule(Guid.NewGuid());
        Assert.False(configBeforeUnknownRemoval.ChangeToken.HasChanged);
        Assert.Single(_sut.GetConfig().Clusters);

        _sut.RemoveModule(moduleId);
        Assert.True(configBeforeUnknownRemoval.ChangeToken.HasChanged);
        Assert.Empty(_sut.GetConfig().Clusters);
        Assert.Empty(_sut.GetConfig().Routes);
    }

    #endregion

    #region Validation Tests

    [Fact]
    public void UpsertModule_EmptyModuleId_ThrowsArgumentException()
    {
        var module = new ProxyModuleDefinition
        {
            ModuleId = Guid.Empty,
            BaseUrlPath = "analytics",
            ContainerPort = 8080
        };

        Assert.Throws<ArgumentException>(() => _sut.UpsertModule(module));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("///")]
    public void UpsertModule_EmptyBaseUrlPath_ThrowsArgumentException(string invalidPath)
    {
        var module = new ProxyModuleDefinition
        {
            ModuleId = Guid.NewGuid(),
            BaseUrlPath = invalidPath,
            ContainerPort = 8080
        };

        Assert.Throws<ArgumentException>(() => _sut.UpsertModule(module));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(65536)]
    public void UpsertModule_OutOfRangePort_ThrowsArgumentOutOfRangeException(int invalidPort)
    {
        var module = new ProxyModuleDefinition
        {
            ModuleId = Guid.NewGuid(),
            BaseUrlPath = "analytics",
            ContainerPort = invalidPort
        };

        Assert.Throws<ArgumentOutOfRangeException>(() => _sut.UpsertModule(module));
    }

    #endregion

    #region End-to-End Forwarding Pipeline Tests

    [Fact]
    public async Task MapModulixReverseProxy_ForwardsActiveEndpointsAndAuthorizationHeaders_Returns404ForUnknownRoutes()
    {
        // Arrange: Start a simulated module container on 127.0.0.1:0
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

        // Arrange: Start the Modulix gateway with JWT auth and YARP reverse proxy
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

        using var httpClient = new HttpClient { BaseAddress = new Uri(gatewayAddress) };
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "container-specific-token");

        // Act & Assert 1: Before registering the module, requests return 404
        var beforeRegistrationResponse = await httpClient.GetAsync("/analytics/api/reports");
        Assert.Equal(HttpStatusCode.NotFound, beforeRegistrationResponse.StatusCode);

        // Act & Assert 2: Dynamically register the running module with one active GET endpoint
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
        Assert.Equal("Bearer container-specific-token", receivedAuthHeader);

        // Act & Assert 3: Unregistered sub-endpoint returns 404; disallowed HTTP method on registered path returns 405 without forwarding
        receivedPath = null;
        var unknownEndpointResponse = await httpClient.GetAsync("/analytics/api/unconfirmed");
        Assert.Equal(HttpStatusCode.NotFound, unknownEndpointResponse.StatusCode);
        Assert.Null(receivedPath);

        var wrongMethodResponse = await httpClient.PostAsync("/analytics/api/reports", content: null);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, wrongMethodResponse.StatusCode);
        Assert.Null(receivedPath);

        // Act & Assert 4: Dynamically removing the module stops routing without restarting
        proxyService.RemoveModule(moduleId);
        var afterRemovalResponse = await httpClient.GetAsync("/analytics/api/reports");
        Assert.Equal(HttpStatusCode.NotFound, afterRemovalResponse.StatusCode);

        await gatewayApp.StopAsync();
        await containerApp.StopAsync();
    }

    #endregion
}
