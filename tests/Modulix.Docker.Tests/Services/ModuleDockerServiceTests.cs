using Microsoft.Extensions.Logging.Abstractions;

using Modulix.Docker.Models;
using Modulix.Docker.Services;
using Modulix.Docker.Services.Interfaces;

namespace Modulix.Docker.Tests.Services;

[Trait("Category", "Services")]
[Trait("SubCategory", "ModuleDockerService")]
public class ModuleDockerServiceTests
{
    [Fact]
    public async Task BuildModuleContainerAsync_WithoutExistingDockerfile_GeneratesFromTemplateAndBuilds()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "Modulix_ModuleDockerService_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var moduleId = Guid.NewGuid();
        var fakeDocker = new FakeInternalDockerService();
        var sut = new ModuleDockerService(fakeDocker, NullLogger<ModuleDockerService>.Instance);

        try
        {
            var containerId = await sut.BuildModuleContainerAsync(moduleId, tempDir, "test-entry.dll", 5000);

            Assert.Equal("built-container-id", containerId);
            Assert.True(fakeDocker.EnsureNetworkCalled);
            Assert.Equal("modulix-network", fakeDocker.EnsuredNetworkName);

            // Verify Dockerfile was generated
            var dockerfilePath = Path.Combine(tempDir, "Dockerfile");
            Assert.True(File.Exists(dockerfilePath));
            var dockerfileContent = await File.ReadAllTextAsync(dockerfilePath);
            Assert.Contains("test-entry.dll", dockerfileContent);

            // Verify BuildContainerAsync arguments
            Assert.Equal(tempDir, fakeDocker.LastBuildContextPath);
            Assert.Equal("Dockerfile", fakeDocker.LastDockerfileName);
            Assert.StartsWith($"modulix-module-{moduleId:N}:", fakeDocker.LastImageName);
            Assert.StartsWith($"modulix-container-{moduleId:N}-", fakeDocker.LastContainerName);
            Assert.Equal("modulix-network", fakeDocker.LastNetworkName);
            Assert.Equal(5000, fakeDocker.LastContainerPort);
            Assert.Contains("ASPNETCORE_HTTP_PORTS=5000", fakeDocker.LastEnv!);
            Assert.Contains("ASPNETCORE_ENVIRONMENT=Production", fakeDocker.LastEnv!);
            Assert.Contains($"{tempDir}/data:/app/data", fakeDocker.LastBinds!);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task BuildModuleContainerAsync_WithExistingDockerfile_PreservesExistingDockerfile()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "Modulix_ModuleDockerService_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var existingContent = "FROM custom-base\nENTRYPOINT [\"custom\"]";
        await File.WriteAllTextAsync(Path.Combine(tempDir, "Dockerfile"), existingContent);

        var moduleId = Guid.NewGuid();
        var fakeDocker = new FakeInternalDockerService();
        var sut = new ModuleDockerService(fakeDocker, NullLogger<ModuleDockerService>.Instance);

        try
        {
            await sut.BuildModuleContainerAsync(moduleId, tempDir, "entry.dll", 8080);

            var currentContent = await File.ReadAllTextAsync(Path.Combine(tempDir, "Dockerfile"));
            Assert.Equal(existingContent, currentContent);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task BuildModuleContainerAsync_VersionedStoragePath_MountsSharedParentDataDirectory()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "Modulix_ModuleDockerService_" + Guid.NewGuid().ToString("N"));
        var moduleId = Guid.NewGuid();
        var versionPath = Path.Combine(tempDir, moduleId.ToString(), "versions", "v1.0.0");
        Directory.CreateDirectory(versionPath);

        var fakeDocker = new FakeInternalDockerService();
        var sut = new ModuleDockerService(fakeDocker, NullLogger<ModuleDockerService>.Instance);

        try
        {
            await sut.BuildModuleContainerAsync(moduleId, versionPath, "entry.dll", 8080);

            var expectedDataPath = Path.Combine(tempDir, moduleId.ToString(), "data");
            Assert.True(Directory.Exists(expectedDataPath));
            Assert.Contains($"{expectedDataPath}:/app/data", fakeDocker.LastBinds!);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task DelegationMethods_ForwardToDockerServiceCorrectly()
    {
        var fakeDocker = new FakeInternalDockerService();
        var sut = new ModuleDockerService(fakeDocker, NullLogger<ModuleDockerService>.Instance);
        var moduleId = Guid.NewGuid();

        await sut.RunModuleContainerAsync("cont1");
        Assert.Equal("run:cont1", fakeDocker.LastCall);

        await sut.WaitUntilReadyAsync("cont1");
        Assert.Equal("ready:cont1", fakeDocker.LastCall);

        await sut.StopModuleContainerAsync("cont1");
        Assert.Equal("stop:cont1", fakeDocker.LastCall);

        await sut.RemoveModuleContainerAsync("cont1", moduleId);
        Assert.Equal($"remove:cont1:{moduleId:N}", fakeDocker.LastCall);

        var tryRemoveResult = await sut.TryRemoveModuleContainerAsync("cont1", moduleId);
        Assert.True(tryRemoveResult);
        Assert.Equal($"try-remove:cont1:{moduleId:N}", fakeDocker.LastCall);

        var isRunning = await sut.IsModuleRunningAsync("cont1");
        Assert.True(isRunning);
        Assert.Equal("running:cont1", fakeDocker.LastCall);

        var stats = await sut.GetModuleStatsAsync("cont1");
        Assert.NotNull(stats);
        Assert.Equal("stats:cont1", fakeDocker.LastCall);
    }

    private sealed class FakeInternalDockerService : IDockerService
    {
        public bool EnsureNetworkCalled { get; private set; }
        public string? EnsuredNetworkName { get; private set; }
        public string? LastBuildContextPath { get; private set; }
        public string? LastDockerfileName { get; private set; }
        public string? LastImageName { get; private set; }
        public string? LastContainerName { get; private set; }
        public string? LastNetworkName { get; private set; }
        public int LastContainerPort { get; private set; }
        public IList<string>? LastEnv { get; private set; }
        public IList<string>? LastBinds { get; private set; }
        public string? LastCall { get; private set; }

        public Task<string> BuildContainerAsync(
            string buildContextPath,
            string dockerfileName,
            string imageName,
            string containerName,
            string networkName,
            int containerPort,
            IList<string>? env = null,
            IList<string>? binds = null,
            CancellationToken ct = default)
        {
            LastBuildContextPath = buildContextPath;
            LastDockerfileName = dockerfileName;
            LastImageName = imageName;
            LastContainerName = containerName;
            LastNetworkName = networkName;
            LastContainerPort = containerPort;
            LastEnv = env;
            LastBinds = binds;
            return Task.FromResult("built-container-id");
        }

        public Task EnsureNetworkExistsAsync(string networkName, CancellationToken ct = default)
        {
            EnsureNetworkCalled = true;
            EnsuredNetworkName = networkName;
            return Task.CompletedTask;
        }

        public Task RunContainerAsync(string containerId, CancellationToken ct = default)
        {
            LastCall = "run:" + containerId;
            return Task.CompletedTask;
        }

        public Task WaitUntilReadyAsync(string containerId, CancellationToken ct = default)
        {
            LastCall = "ready:" + containerId;
            return Task.CompletedTask;
        }

        public Task StopContainerAsync(string containerId, CancellationToken ct = default)
        {
            LastCall = "stop:" + containerId;
            return Task.CompletedTask;
        }

        public Task RemoveContainerAsync(string containerId, string containerName, CancellationToken ct = default)
        {
            LastCall = $"remove:{containerId}:{containerName}";
            return Task.CompletedTask;
        }

        public Task<bool> TryRemoveModuleContainerAsync(string containerId, string containerName, CancellationToken ct = default)
        {
            LastCall = $"try-remove:{containerId}:{containerName}";
            return Task.FromResult(true);
        }

        public Task<bool> IsContainerRunningAsync(string containerId, CancellationToken ct = default)
        {
            LastCall = "running:" + containerId;
            return Task.FromResult(true);
        }

        public Task<ContainerStats> GetContainerStatsAsync(string containerId, CancellationToken ct = default)
        {
            LastCall = "stats:" + containerId;
            return Task.FromResult(new ContainerStats { IsRunning = true, CpuUsagePercent = 5.0 });
        }
    }
}
