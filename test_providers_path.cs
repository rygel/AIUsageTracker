using System;
using System.IO;

class TestProvidersPath
{
    static void Main()
    {
        var providersPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share", "opencode", "providers.json");
        Console.WriteLine($"Checking path: {providersPath}");
        Console.WriteLine($"File exists: {File.Exists(providersPath)}");
        
        if (File.Exists(providersPath))
        {
            try
            {
                var content = File.ReadAllText(providersPath);
                Console.WriteLine($"Content length: {content.Length}");
                Console.WriteLine($"Contains synthetic: {content.Contains("synthetic")}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading: {ex.Message}");
            }
        }
        
        // Also check .config path
        var configPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "opencode", "providers.json");
        Console.WriteLine($"\nChecking config path: {configPath}");
        Console.WriteLine($"File exists: {File.Exists(configPath)}");
    }
}
