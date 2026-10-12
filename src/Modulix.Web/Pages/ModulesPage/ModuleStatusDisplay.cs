using Modulix.Client.Models.Enums;

namespace Modulix.Web.Pages.ModulesPage;

public static class ModuleStatusDisplay
{
    public static string StatusClass(ModuleStatus status) => status switch
    {
        ModuleStatus.PendingConfirmation => "pending",
        ModuleStatus.QueuedForScan => "queued",
        _ => status.ToString().ToLowerInvariant()
    };

    public static string StatusLabel(ModuleStatus status) => status switch
    {
        ModuleStatus.PendingConfirmation => "Pending confirmation",
        ModuleStatus.QueuedForScan => "Queued for scan",
        _ => status.ToString()
    };
}