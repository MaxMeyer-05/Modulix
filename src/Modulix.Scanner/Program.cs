using System.Text.Json;
using Modulix.Scanner.Services;

var targetPath = args.FirstOrDefault();

if (string.IsNullOrWhiteSpace(targetPath) || !Directory.Exists(targetPath))
{
    Console.Error.WriteLine($"[Modulix.Scanner] Error: Directory does not exist or was not specified: '{targetPath}'");
    return 1;
}

try
{
    var scanner = new ModuleEndpointScanner();
    var result = await scanner.ScanDirectoryAsync(targetPath);

    var json = JsonSerializer.Serialize(result, new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    });

    Console.WriteLine(json);
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"[Modulix.Scanner] Scan failed: {ex.Message}");
    return 2;
}