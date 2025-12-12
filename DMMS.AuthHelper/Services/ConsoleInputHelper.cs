using DMMS.AuthHelper.Models;
using Microsoft.Extensions.Logging;

namespace DMMS.AuthHelper.Services;

/// <summary>
/// Helper for secure console input operations
/// </summary>
public static class ConsoleInputHelper
{
    /// <summary>
    /// Prompts the user for DoltHub credentials via console
    /// </summary>
    /// <param name="endpoint">The DoltHub endpoint</param>
    /// <param name="logger">Logger for operation tracking</param>
    /// <returns>The entered credentials or null if cancelled</returns>
    public static DoltHubCredentials? PromptForDoltHubCredentials(string endpoint, ILogger? logger = null)
    {
        try
        {
            Console.WriteLine();
            Console.WriteLine("=== DoltHub Authentication Required ===");
            Console.WriteLine($"Endpoint: {endpoint}");
            Console.WriteLine();
            
            // Prompt for username
            Console.Write("DoltHub Username: ");
            var username = Console.ReadLine();
            
            if (string.IsNullOrWhiteSpace(username))
            {
                Console.WriteLine("Error: Username cannot be empty.");
                return null;
            }

            // Prompt for API token (hidden input)
            Console.Write("API Token (hidden): ");
            var apiToken = ReadPasswordFromConsole();
            
            if (string.IsNullOrWhiteSpace(apiToken))
            {
                Console.WriteLine("\nError: API Token cannot be empty.");
                return null;
            }

            Console.WriteLine("\nCredentials captured successfully.");
            logger?.LogInformation("User provided credentials for username: {Username} on endpoint: {Endpoint}", username, endpoint);
            
            return new DoltHubCredentials(username, apiToken, endpoint);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Error during credential input for endpoint: {Endpoint}", endpoint);
            Console.WriteLine($"\nError during credential input: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Reads password from console with hidden characters
    /// </summary>
    /// <returns>The entered password</returns>
    private static string ReadPasswordFromConsole()
    {
        var password = string.Empty;
        ConsoleKeyInfo key;

        do
        {
            key = Console.ReadKey(true);

            if (key.Key != ConsoleKey.Backspace && key.Key != ConsoleKey.Enter)
            {
                password += key.KeyChar;
                Console.Write("*");
            }
            else if (key.Key == ConsoleKey.Backspace && password.Length > 0)
            {
                password = password.Substring(0, password.Length - 1);
                Console.Write("\b \b");
            }
        }
        while (key.Key != ConsoleKey.Enter);

        return password;
    }

    /// <summary>
    /// Prompts user for confirmation
    /// </summary>
    /// <param name="message">The confirmation message</param>
    /// <param name="defaultValue">Default value if user just presses enter</param>
    /// <returns>True if confirmed, false otherwise</returns>
    public static bool PromptForConfirmation(string message, bool defaultValue = false)
    {
        var defaultText = defaultValue ? "[Y/n]" : "[y/N]";
        Console.Write($"{message} {defaultText}: ");
        
        var input = Console.ReadLine()?.Trim().ToLowerInvariant();
        
        if (string.IsNullOrEmpty(input))
            return defaultValue;
            
        return input is "y" or "yes" or "true";
    }

    /// <summary>
    /// Displays help information for the authentication process
    /// </summary>
    public static void DisplayAuthenticationHelp()
    {
        Console.WriteLine();
        Console.WriteLine("=== DMMS Authentication Helper ===");
        Console.WriteLine();
        Console.WriteLine("This tool helps you securely authenticate with DoltHub.");
        Console.WriteLine();
        Console.WriteLine("To get your API token:");
        Console.WriteLine("1. The browser will open to DoltHub's token settings page");
        Console.WriteLine("2. Sign in to your DoltHub account");
        Console.WriteLine("3. Create a new API token or copy an existing one");
        Console.WriteLine("4. Return to this window and enter your credentials");
        Console.WriteLine();
        Console.WriteLine("Your credentials will be stored securely in Windows Credential Manager.");
        Console.WriteLine();
    }

    /// <summary>
    /// Displays operation result
    /// </summary>
    /// <param name="result">The operation result</param>
    /// <param name="operation">Description of the operation</param>
    public static void DisplayResult(CredentialResult result, string operation)
    {
        Console.WriteLine();
        if (result.IsSuccess)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"✓ {operation} completed successfully.");
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"✗ {operation} failed: {result.ErrorMessage}");
        }
        Console.ResetColor();
        Console.WriteLine();
    }
}