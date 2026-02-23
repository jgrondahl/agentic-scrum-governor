namespace GovernorCli.Application.UseCases.Deliver;

/// <summary>
/// Minimal deterministic fixture template generator.
/// FIXTURE ONLY - used for pipeline verification, not production.
/// Generates a minimal .NET console app under workspace/{appId}/apps/{appId}/.
/// </summary>
public static class FixtureDotNetTemplateGenerator
{
    public const string TemplateId = "fixture_dotnet_console_hello";

    public static void Generate(string workspaceAppRoot, string appId)
    {
        // Create minimal project structure
        Directory.CreateDirectory(workspaceAppRoot);

        // Write .csproj file
        var csprojContent = GenerateProjectFile(appId);
        File.WriteAllText(Path.Combine(workspaceAppRoot, $"{appId}.csproj"), csprojContent);

        // Write Program.cs
        var programContent = GenerateProgramFile();
        File.WriteAllText(Path.Combine(workspaceAppRoot, "Program.cs"), programContent);

        // Create global usings
        var usingsContent = "global using System;\n";
        File.WriteAllText(Path.Combine(workspaceAppRoot, "GlobalUsings.cs"), usingsContent);
    }

    private static string GenerateProjectFile(string appId)
    {
        return $"""
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>

</Project>
""";
    }

    private static string GenerateProgramFile()
    {
        return """
// Fixture: minimal console app for Deliver pipeline validation
Console.WriteLine("Hello from Deliver fixture!");
Console.WriteLine($"Generated at: {DateTime.UtcNow:O}");
return 0;
""";
    }
}
