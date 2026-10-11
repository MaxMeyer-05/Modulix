using Modulix.Client.Models.Dtos;
using Modulix.Client.Models.Enums;

namespace Modulix.Web.Pages.Modules;

internal static class ModulePreview
{
    public static IReadOnlyList<ModuleDetailDto> Modules { get; } =
    [
        new()
        {
            Id = Guid.Parse("3fa85f64-5717-4562-b3fc-2c963f66afa6"),
            ModuleName = "AnalyticsModule",
            Description = "Usage analytics, reporting, and workspace insights.",
            BaseEndpointPath = "/api/analytics", ContainerPort = 8080, Status = ModuleStatus.Running,
            CreatedAtUtc = new DateTime(2026, 10, 10, 9, 30, 0, DateTimeKind.Utc),
            ContainerId = "7e2a19c8b3d45f60a1c29e74d5b83fa6",
            StoragePath = "/var/lib/modulix/modules/analytics",
            Endpoints =
            [
                new() { Id = Guid.Parse("0f96e4e0-fb0d-4bbc-8766-4123f91db52c"), HttpMethod = "GET", EndpointPath = "/summary", Status = ModuleEndpointsStatus.Active, CreatedAtUtc = new DateTime(2026, 10, 10, 9, 30, 0, DateTimeKind.Utc) },
                new() { Id = Guid.Parse("6af9b9b6-3ff3-491b-af0d-3e9c02ad1649"), HttpMethod = "GET", EndpointPath = "/reports/{id}", Status = ModuleEndpointsStatus.Active, CreatedAtUtc = new DateTime(2026, 10, 10, 9, 30, 0, DateTimeKind.Utc) },
                new() { Id = Guid.Parse("dbd6417d-8b31-409a-a1eb-b2432fb3a185"), HttpMethod = "POST", EndpointPath = "/events", Status = ModuleEndpointsStatus.Active, CreatedAtUtc = new DateTime(2026, 10, 10, 9, 30, 0, DateTimeKind.Utc) }
            ]
        },
        new()
        {
            Id = Guid.Parse("69df4547-2e52-4f7a-8c5d-4296f017eaf2"), ModuleName = "IdentityService",
            Description = "Identity profiles and access management for connected services.",
            BaseEndpointPath = "/api/identity", ContainerPort = 8081, Status = ModuleStatus.Starting,
            CreatedAtUtc = new DateTime(2026, 10, 9, 14, 15, 0, DateTimeKind.Utc),
            ContainerId = "a8df092731bc4de68571f9c320abe648", StoragePath = "/var/lib/modulix/modules/identity"
        },
        new()
        {
            Id = Guid.Parse("2dca0f63-35c1-4bfb-9ebc-20630787d2b5"), ModuleName = "ClientExtension",
            Description = "Additional client integrations and custom workspace capabilities.",
            BaseEndpointPath = "/api/client", ContainerPort = 8082, Status = ModuleStatus.PendingConfirmation,
            CreatedAtUtc = new DateTime(2026, 10, 8, 11, 45, 0, DateTimeKind.Utc), StoragePath = "/var/lib/modulix/modules/client",
            Endpoints = [new() { Id = Guid.Parse("ddbb9e8d-a245-424f-a501-7b690121b097"), HttpMethod = "GET", EndpointPath = "/extensions", Status = ModuleEndpointsStatus.PendingConfirmation, CreatedAtUtc = new DateTime(2026, 10, 8, 11, 45, 0, DateTimeKind.Utc) }]
        },
        new()
        {
            Id = Guid.Parse("974b3765-436f-4f21-afd0-b795252e9f78"), ModuleName = "ReportsModule",
            Description = "Scheduled reports and exports for operational data.",
            BaseEndpointPath = "/api/reports", ContainerPort = 8083, Status = ModuleStatus.QueuedForScan,
            CreatedAtUtc = new DateTime(2026, 10, 7, 8, 20, 0, DateTimeKind.Utc), StoragePath = "/var/lib/modulix/modules/reports"
        },
        new()
        {
            Id = Guid.Parse("453c057e-73a1-4e07-84b4-0414c8f745ca"), ModuleName = "StorageModule",
            Description = "File storage and document delivery for workspace modules.",
            BaseEndpointPath = "/api/storage", ContainerPort = 8084, Status = ModuleStatus.Running,
            CreatedAtUtc = new DateTime(2026, 10, 6, 16, 10, 0, DateTimeKind.Utc),
            ContainerId = "d47c852091ef4ab6935db820fe175a6c", StoragePath = "/var/lib/modulix/modules/storage",
            Endpoints = [new() { Id = Guid.Parse("c1d3b6b7-f83a-4d3e-9651-4420d491c533"), HttpMethod = "GET", EndpointPath = "/files/{id}", Status = ModuleEndpointsStatus.Active, CreatedAtUtc = new DateTime(2026, 10, 6, 16, 10, 0, DateTimeKind.Utc) }]
        },
        new()
        {
            Id = Guid.Parse("8a1d69c7-4f18-4970-9e59-f503c2f3441b"), ModuleName = "BillingService",
            Description = "Subscription records, invoices, and payment processing.",
            BaseEndpointPath = "/api/billing", ContainerPort = 8085, Status = ModuleStatus.Stopped,
            CreatedAtUtc = new DateTime(2026, 10, 5, 10, 0, 0, DateTimeKind.Utc), StoragePath = "/var/lib/modulix/modules/billing"
        }
    ];

    public static string StatusClass(ModuleStatus status) => status switch
    {
        ModuleStatus.Running => "running",
        ModuleStatus.Starting => "starting",
        ModuleStatus.PendingConfirmation => "pending",
        ModuleStatus.QueuedForScan => "queued",
        ModuleStatus.Failed => "failed",
        _ => "neutral"
    };

    public static string StatusLabel(ModuleStatus status) => status switch
    {
        ModuleStatus.PendingConfirmation => "Pending confirmation",
        ModuleStatus.QueuedForScan => "Queued for scan",
        _ => status.ToString()
    };
}