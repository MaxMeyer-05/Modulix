using Server.Models.Dtos;

namespace Server.TestData;

#region Base Endpoint Path Test Data

/// <summary>
/// Provides invalid, empty or whitespace-only base endpoint paths.
/// </summary>
public class InvalidBaseEndpointPathsTestData : TheoryData<string?>
{
    public InvalidBaseEndpointPathsTestData()
    {
        Add(null);
        Add(string.Empty);
        Add("   ");
        Add("\t\n");
    }
}

/// <summary>
/// Provides path collision scenarios (prefix shadowing and sub-path conflicts).
/// Format: ExistingPath, NewCollidingPath
/// </summary>
public class ConflictingBaseEndpointPathsTestData : TheoryData<string, string>
{
    public ConflictingBaseEndpointPathsTestData()
    {
        // Exact duplicate (after normalization)
        Add("api/v1/resource", "api/v1/resource");
        Add("api/v1/resource", "/api/v1/resource/");

        // New path is a sub-path of existing path
        Add("api/v1/resource", "api/v1/resource/sub");
        Add("api/v1/resource", "/api/v1/resource/endpoints/test");

        // Existing path is a sub-path of new path
        Add("api/v1/resource/extended", "api/v1/resource");
    }
}

#endregion

#region File Validation Test Data

/// <summary>
/// Provides invalid file name extensions that should be rejected.
/// </summary>
public class InvalidModuleArchiveExtensionsTestData : TheoryData<string>
{
    public InvalidModuleArchiveExtensionsTestData()
    {
        Add("module.tar");
        Add("module.tar.gz");
        Add("module.rar");
        Add("module.dll");
        Add("module.exe");
        Add("module.txt");
    }
}

#endregion

#region Module Metadata Update Test Data

/// <summary>
/// Provides valid update DTO combinations for module metadata.
/// </summary>
public class ValidModuleUpdateTestData : TheoryData<UpdateModuleDto, string, string?>
{
    public ValidModuleUpdateTestData()
    {
        // Update both name and description
        Add(
            new UpdateModuleDto { ModuleName = "Updated Name", Description = "Updated Description" },
            "Updated Name",
            "Updated Description"
        );

        // Update only name
        Add(
            new UpdateModuleDto { ModuleName = "Only Name Updated", Description = null },
            "Only Name Updated",
            "Original Description"
        );

        // Update only description
        Add(
            new UpdateModuleDto { ModuleName = null, Description = "Only Description Updated" },
            "Original Name",
            "Only Description Updated"
        );
    }
}

#endregion