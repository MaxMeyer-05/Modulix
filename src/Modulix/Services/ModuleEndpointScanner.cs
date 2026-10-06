using System.Text.Json;
using System.Collections.Concurrent;

using Docker.DotNet;
using Docker.DotNet.Models;

using Microsoft.Extensions.Options;

using Modulix.Models.Dtos;
using Modulix.Models.Enums;
using Modulix.Models.Options;

using Modulix.Services.Interfaces;
using Modulix.Database.DbContexts;

namespace Modulix.Services;

/// <inheritdoc cref="IModuleEndpointScanner"/>
public class ModuleEndpointScanner : IModuleEndpointScanner, IDisposable
{
    /// <summary>
    /// The shared module settings.
    /// </summary>
    private readonly ModulixOptions _options;


    /// <summary>
    /// Semaphore used to control the concurrency of scan jobs.
    /// </summary>
    private readonly SemaphoreSlim _concurrencySemaphore;

    /// <summary>
    /// Semaphore used to signal when items are available in the priority queue.
    /// </summary>
    private readonly SemaphoreSlim _itemsAvailable = new(0);

    /// <summary>
    /// Dictionary that maps module IDs to their corresponding cancellation token sources for queued jobs.
    /// </summary>
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _queuedJobs = new();

    /// <summary>
    /// Priority queue that holds the scan jobs.
    /// </summary>
    private readonly PriorityQueue<ScanJob, int> _priorityQueue = new();

    /// <summary>
    /// Lock object used to synchronize access to the priority queue and other shared resources.
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
    /// The server context.
    /// </summary>
    private readonly ServerContext _context;

    /// <summary>
    /// The module service for handling module-related operations.
    /// </summary>
    private readonly IModuleService _moduleService;

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
        /// The cancellation token used to cancel the scan job.
        /// </summary>
        public required CancellationToken CancellationToken { get; init; }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ModuleEndpointScanner"/> class.
    /// </summary>
    /// <param name="dockerClient">The Docker client used to interact with Docker containers.</param>
    /// <param name="logger">The logger instance for logging information and errors.</param>
    /// <param name="context">The server context for accessing the database and other services.</param>
    /// <param name="moduleService">The module service for handling module-related operations.</param>
    /// <param name="options">The shared module settings.</param>
    public ModuleEndpointScanner(
        DockerClient dockerClient, 
        ILogger<ModuleEndpointScanner> logger, 
        ServerContext context, 
        IModuleService moduleService,
        IOptions<ModulixOptions> options)
    {
        _dockerClient = dockerClient;
        _logger = logger;
        _context = context;
        _moduleService = moduleService;
        _options = options.Value;
        _concurrencySemaphore = new SemaphoreSlim(_options.MaxConcurrentScans, _options.MaxConcurrentScans);
        _ = ProcessQueueLoopAsync(_cts.Token);
    }

    /// <inheritdoc/>
    public async Task<ScanEnqueueResponse> ScanOrEnqueueAsync(Guid moduleId, string moduleDirectoryPath, ScanPriority priority = ScanPriority.Create, CancellationToken ct = default)
    {
        if (!Directory.Exists(moduleDirectoryPath))
            throw new DirectoryNotFoundException($"Directory not found: {moduleDirectoryPath}");

        bool executeImmediately = false;
        
        lock (_lock)
        {
            if (_priorityQueue.Count == 0 && _concurrencySemaphore.Wait(0))
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

        var jobCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
        _queuedJobs.TryAdd(moduleId, jobCts);

        var job = new ScanJob
        {
            ModuleId = moduleId,
            HostDirectoryPath = Path.GetFullPath(moduleDirectoryPath),
            Priority = priority,
            CancellationToken = jobCts.Token
        };

        lock (_lock)
        {
            _priorityQueue.Enqueue(job, (int)priority);
            _logger.LogInformation("All containers busy. Enqueued scan for module '{ModuleId}'. Queue length: {Count}", moduleId, _priorityQueue.Count);
        }

        _itemsAvailable.Release();
        return new ScanEnqueueResponse { WasQueued = true };
    }

    /// <inheritdoc/>
    public bool CancelScan(Guid moduleId)
    {
        if (_queuedJobs.TryGetValue(moduleId, out var cts))
        {
            cts.Cancel(); 
            _queuedJobs.TryRemove(moduleId, out _);
            return true;
        }
        return false;
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
                    while (_priorityQueue.Count > 0)
                    {
                        var candidate = _priorityQueue.Dequeue();
                        if (!candidate.CancellationToken.IsCancellationRequested)
                        {
                            nextJob = candidate;
                            break;
                        }
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
                        var result = await RunDockerScannerAsync(nextJob.HostDirectoryPath, nextJob.CancellationToken);
                        await _moduleService.ProcessQueuedScanResultAsync(nextJob.ModuleId, result, stoppingToken);
                    }
                    catch (OperationCanceledException)
                    {
                        _logger.LogInformation("Scan for module {ModuleId} was cancelled.", nextJob.ModuleId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Background scan failed for module {ModuleId}.", nextJob.ModuleId);
                        
                        var module = await _context.Modules.FindAsync([nextJob.ModuleId], stoppingToken);
                        if (module != null)
                        {
                            module.Status = ModuleStatus.Failed;
                            await _context.SaveChangesAsync(stoppingToken);
                        }
                    }
                    finally
                    {
                        _queuedJobs.TryRemove(nextJob.ModuleId, out _);
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
        _concurrencySemaphore.Dispose();
        _itemsAvailable.Dispose();
        _cts.Dispose();
    }
}