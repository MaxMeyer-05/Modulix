using System.Text.Json;

using Docker.DotNet;
using Docker.DotNet.Models;

using Microsoft.Extensions.Options;

using Modulix.Client.Models.Dtos;
using Modulix.Client.Models.Enums;

using Modulix.Docker.Options;
using Modulix.ReverseProxy.Services;

using Modulix.Services.Interfaces;
using Modulix.Database.DbContexts;

namespace Modulix.Services;

/// <inheritdoc cref="IModuleEndpointScanner"/>
public class ModuleEndpointScanner : IModuleEndpointScanner, IDisposable
{
    /// <summary>
    /// The shared Docker settings.
    /// </summary>
    private readonly DockerOptions _options;

    /// <summary>
    /// Semaphore used to control the concurrency of scan jobs.
    /// </summary>
    private readonly SemaphoreSlim _concurrencySemaphore;

    /// <summary>
    /// Semaphore used to signal when items are available in the scan queue.
    /// </summary>
    private readonly SemaphoreSlim _itemsAvailable = new(0);

    /// <summary>
    /// Unified queue that holds both waiting and currently running background scan jobs.
    /// </summary>
    private readonly List<ScanJob> _scanQueue = [];

    /// <summary>
    /// Lock object used to synchronize access to the scan queue and shared resources.
    /// </summary>
    private readonly object _lock = new();


    /// <summary>
    /// The Docker client used to interact with Docker containers.
    /// </summary>
    private readonly DockerClient _dockerClient;

    /// <summary>
    /// The logger instance.
    /// </summary>
    private readonly ILogger<ModuleEndpointScanner> _logger;

    /// <summary>
    /// The service scope factory for creating scoped services.
    /// </summary>
    private readonly IServiceScopeFactory _scopeFactory;

    /// <summary>
    /// The cancellation token.
    /// </summary>
    private readonly CancellationTokenSource _cts = new();


    /// <summary>
    /// Represents a scan job that contains information about the module to be scanned.
    /// </summary>
    private sealed class ScanJob
    {
        /// <summary>
        /// The unique identifier of the module to be scanned.
        /// </summary>
        public required Guid ModuleId { get; init; }

        /// <summary>
        /// The path to the host directory containing the module's files.
        /// </summary>
        public required string HostDirectoryPath { get; init; }

        /// <summary>
        /// The priority of the scan job.
        /// </summary>
        public required ScanPriority Priority { get; init; }

        /// <summary>
        /// The cancellation token source used to cancel the scan job.
        /// </summary>
        public required CancellationTokenSource CancellationTokenSource { get; init; }

        /// <summary>
        /// Indicates whether the scan job has been dequeued and is currently running.
        /// </summary>
        public bool IsRunning { get; set; }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ModuleEndpointScanner"/> class.
    /// </summary>
    /// <param name="dockerClient">The Docker client used to interact with Docker containers.</param>
    /// <param name="logger">The logger instance for logging information and errors.</param>
    /// <param name="scopeFactory">The service scope factory for creating scoped services.</param>
    /// <param name="options">The shared Docker settings.</param>
    public ModuleEndpointScanner(
        DockerClient dockerClient, 
        ILogger<ModuleEndpointScanner> logger,
        IServiceScopeFactory scopeFactory,
        IOptions<DockerOptions> options)
    {
        _dockerClient = dockerClient;
        _logger = logger;
        _options = options.Value;
        _scopeFactory = scopeFactory;
        _concurrencySemaphore = new SemaphoreSlim(_options.MaxConcurrentScans, _options.MaxConcurrentScans);
        _ = ProcessQueueLoopAsync(_cts.Token);
    }

    /// <inheritdoc/>
    public async Task<ScanEnqueueResponse> ScanOrEnqueueAsync(Guid moduleId, string moduleDirectoryPath, ScanPriority priority = ScanPriority.Create, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (!Directory.Exists(moduleDirectoryPath))
            throw new DirectoryNotFoundException($"Directory not found: {moduleDirectoryPath}");

        bool executeImmediately = false;
        
        lock (_lock)
        {
            if (!_scanQueue.Any(job => !job.IsRunning) && _concurrencySemaphore.Wait(0))
            {
                executeImmediately = true;
            }
        }

        if (executeImmediately)
        {
            try
            {
                var result = await RunDockerScannerAsync(moduleDirectoryPath, ct);
                return new ScanEnqueueResponse { WasQueued = false, Result = result };
            }
            finally
            {
                _concurrencySemaphore.Release();
            }
        }

        var jobCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, ct);

        var job = new ScanJob
        {
            ModuleId = moduleId,
            HostDirectoryPath = Path.GetFullPath(moduleDirectoryPath),
            Priority = priority,
            CancellationTokenSource = jobCts
        };

        lock (_lock)
        {
            _scanQueue.Add(job);
            _logger.LogInformation(
                "All containers busy. Enqueued scan for module '{ModuleId}'. Queue length: {Count}",
                moduleId,
                _scanQueue.Count(j => !j.IsRunning));
        }

        _itemsAvailable.Release();
        return new ScanEnqueueResponse { WasQueued = true };
    }

    /// <inheritdoc/>
    public bool CancelScan(Guid moduleId)
    {
        ScanJob? job;
        lock (_lock)
        {
            job = _scanQueue.FirstOrDefault(j => j.ModuleId == moduleId);
            if (job is null)
                return false;

            _scanQueue.Remove(job);
        }

        job.CancellationTokenSource.Cancel();
        if (!job.IsRunning)
        {
            job.CancellationTokenSource.Dispose();
        }
        return true;
    }

    /// <inheritdoc/>
    public IReadOnlyList<ModuleScanQueueItemDto> GetQueuedScans()
    {
        lock (_lock)
        {
            return _scanQueue
                .Where(job => !job.CancellationTokenSource.IsCancellationRequested)
                .OrderByDescending(job => job.IsRunning)
                .ThenBy(job => job.Priority)
                .Select(job => new ModuleScanQueueItemDto
                {
                    ModuleId = job.ModuleId,
                    Priority = job.Priority,
                    IsRunning = job.IsRunning
                })
                .ToList();
        }
    }

    /// <summary>
    /// Continuously processes the scan job queue, 
    /// executing jobs as resources become available.
    /// </summary>
    /// <param name="stoppingToken">The cancellation token to observe while processing the queue.</param>
    private async Task ProcessQueueLoopAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _itemsAvailable.WaitAsync(stoppingToken);
                await _concurrencySemaphore.WaitAsync(stoppingToken);

                ScanJob? nextJob = null;

                lock (_lock)
                {
                    while (true)
                    {
                        var candidate = _scanQueue
                            .Where(job => !job.IsRunning)
                            .OrderBy(job => job.Priority)
                            .FirstOrDefault();

                        if (candidate is null)
                            break;

                        if (candidate.CancellationTokenSource.IsCancellationRequested)
                        {
                            _scanQueue.Remove(candidate);
                            candidate.CancellationTokenSource.Dispose();
                            continue;
                        }

                        candidate.IsRunning = true;
                        nextJob = candidate;
                        break;
                    }
                }

                if (nextJob is null)
                {
                    _concurrencySemaphore.Release();
                    continue;
                }

                _ = Task.Run(async () =>
                {
                    try
                    {
                        var result = await RunDockerScannerAsync(nextJob.HostDirectoryPath, nextJob.CancellationTokenSource.Token);

                        await using var scope = _scopeFactory.CreateAsyncScope();
                        var _moduleService = scope.ServiceProvider.GetRequiredService<IModuleService>();

                        await _moduleService.ProcessQueuedScanResultAsync(nextJob.ModuleId, result, nextJob.CancellationTokenSource.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        _logger.LogInformation("Scan for module {ModuleId} was cancelled.", nextJob.ModuleId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Background scan failed for module {ModuleId}.", nextJob.ModuleId);
                        
                        await using var scope = _scopeFactory.CreateAsyncScope();
                        var _context = scope.ServiceProvider.GetRequiredService<ServerContext>();
                        
                        var module = await _context.Modules.FindAsync([nextJob.ModuleId], stoppingToken);
                        if (module != null)
                        {
                            module.Status = ModuleStatus.Failed;
                            await _context.SaveChangesAsync(stoppingToken);
                            scope.ServiceProvider.GetService<IReverseProxyService>()?.RemoveModule(module.Id);
                        }
                    }
                    finally
                    {
                        lock (_lock)
                        {
                            _scanQueue.Remove(nextJob);
                        }
                        nextJob.CancellationTokenSource.Dispose();
                        _concurrencySemaphore.Release();
                    }
                }, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    /// <summary>
    /// Runs the Docker-based scanner on the specified host directory 
    /// and returns the scan result.
    /// </summary>
    /// <param name="hostDirectoryPath">The path to the host directory to scan.</param>
    /// <param name="ct">The cancellation token to observe while running the scanner.</param>
    /// <returns>The result of the module scan.</returns>
    /// <exception cref="InvalidOperationException">Thrown if the scanner container fails or the scan result cannot be parsed.</exception>
    private async Task<ModuleScanResultDto> RunDockerScannerAsync(string hostDirectoryPath, CancellationToken ct)
    {
        var containerName = $"modulix-scanner-{Guid.NewGuid():N}";

        var createResponse = await _dockerClient.Containers.CreateContainerAsync(new CreateContainerParameters
        {
            Image = _options.ScannerImage,
            Name = containerName,
            Cmd = ["/scan-target"],
            NetworkDisabled = true,
            HostConfig = new HostConfig
            {
                Binds = [$"{hostDirectoryPath}:/scan-target:ro"],
                AutoRemove = false,
                CapDrop = ["ALL"]
            }
        }, ct);

        try
        {
            await _dockerClient.Containers.StartContainerAsync(createResponse.ID, new ContainerStartParameters(), ct);
            var waitResponse = await _dockerClient.Containers.WaitContainerAsync(createResponse.ID, ct);

            var logStream = await _dockerClient.Containers.GetContainerLogsAsync(createResponse.ID, false, new ContainerLogsParameters
            {
                ShowStdout = true,
                ShowStderr = true
            }, ct);

            var (stdout, stderr) = await ReadLogsAsync(logStream);

            if (waitResponse.StatusCode != 0)
                throw new InvalidOperationException($"Scanner container failed (Code {waitResponse.StatusCode}): {stderr}");

            var result = JsonSerializer.Deserialize<ModuleScanResultDto>(stdout, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (result is null || string.IsNullOrWhiteSpace(result.EntryAssemblyFileName))
                throw new InvalidOperationException("Could not parse valid scan result from container.");

            return result;
        }
        finally
        {
            try
            {
                await _dockerClient.Containers.RemoveContainerAsync(
                    createResponse.ID, 
                    new ContainerRemoveParameters { Force = true }, 
                    CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to remove scanner container '{ContainerName}'.", containerName);
            }
        }
    }

    /// <summary>
    /// Reads the logs from the specified multiplexed stream 
    /// and returns the standard output and standard error as strings.
    /// </summary>
    /// <param name="stream">The multiplexed stream containing the container logs.</param>
    /// <returns>A tuple containing the standard output and standard error as strings.</returns>
    private static async Task<(string Stdout, string Stderr)> ReadLogsAsync(MultiplexedStream stream)
    {
        using var stdoutMemory = new MemoryStream();
        using var stderrMemory = new MemoryStream();
        await stream.CopyOutputToAsync(null, stdoutMemory, stderrMemory, CancellationToken.None);

        stdoutMemory.Position = 0;
        stderrMemory.Position = 0;
        using var readerOut = new StreamReader(stdoutMemory);
        using var readerErr = new StreamReader(stderrMemory);

        return (await readerOut.ReadToEndAsync(), await readerErr.ReadToEndAsync());
    }

    /// <summary>
    /// Disposes the resources used by the module endpoint scanner.
    /// </summary>
    public void Dispose()
    {
        _cts.Cancel();
        lock (_lock)
        {
            foreach (var job in _scanQueue.Where(j => !j.IsRunning))
            {
                job.CancellationTokenSource.Dispose();
            }
            _scanQueue.RemoveAll(j => !j.IsRunning);
        }
        _concurrencySemaphore.Dispose();
        _itemsAvailable.Dispose();
        _cts.Dispose();
    }
}