using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using UMCPServer.Tools;

namespace UMCPServer.TestConsole;

/// <summary>
/// Standalone test program for the InterpretTestResults tool
/// </summary>
public class TestInterpretTool
{
    public static async Task Main(string[] args)
    {
        Console.WriteLine("Testing InterpretTestResults Tool");
        
        // Create a simple console logger
        using var loggerFactory = LoggerFactory.Create(builder =>
            builder.AddConsole().SetMinimumLevel(LogLevel.Information));
        var logger = loggerFactory.CreateLogger<InterpretTestResultsTool>();
        
        // Create the tool
        var tool = new InterpretTestResultsTool(logger);
        
        // Test with the XML file
        var xmlPath = Path.GetFullPath(Path.Combine("..", "..", "TestResults", 
            "TestResults_EditMode_RunTests_8f8bb0b4807a463eb08d2f99b54f42ca_20250708_103845.xml"));
        
        Console.WriteLine($"Testing with XML file: {xmlPath}");
        Console.WriteLine($"File exists: {File.Exists(xmlPath)}");
        
        if (!File.Exists(xmlPath))
        {
            Console.WriteLine("ERROR: XML file not found!");
            return;
        }
        
        // Test 1: General statistics
        Console.WriteLine("\n=== Test 1: General Statistics ===");
        var result1 = await tool.InterpretTestResults(xmlPath);
        Console.WriteLine($"Success: {GetProperty(result1, "success")}");
        if (GetProperty(result1, "success").ToString() == "True")
        {
            var stats = GetProperty(result1, "statistics");
            Console.WriteLine($"Total Tests: {GetProperty(stats, "totalTests")}");
            Console.WriteLine($"Passed: {GetProperty(stats, "passed")}");
            Console.WriteLine($"Failed: {GetProperty(stats, "failed")}");
        }
        else
        {
            Console.WriteLine($"Error: {GetProperty(result1, "error")}");
        }
        
        // Test 2: Failed tests only
        Console.WriteLine("\n=== Test 2: Failed Tests Only ===");
        var result2 = await tool.InterpretTestResults(xmlPath, failedOnly: true, includeStackTraces: true);
        Console.WriteLine($"Success: {GetProperty(result2, "success")}");
        if (GetProperty(result2, "success").ToString() == "True")
        {
            var testCases = GetProperty(result2, "testCases") as System.Collections.IList;
            Console.WriteLine($"Failed tests found: {testCases?.Count ?? 0}");
            if (testCases != null)
            {
                foreach (var testCase in testCases)
                {
                    Console.WriteLine($"  - {GetProperty(testCase, "name")}");
                }
            }
        }
        
        // Test 3: Search for specific output
        Console.WriteLine("\n=== Test 3: Search for Area Output ===");
        var result3 = await tool.InterpretTestResults(xmlPath, searchInOutput: "Area=0.315219", includeOutput: true);
        Console.WriteLine($"Success: {GetProperty(result3, "success")}");
        if (GetProperty(result3, "success").ToString() == "True")
        {
            var testCases = GetProperty(result3, "testCases") as System.Collections.IList;
            Console.WriteLine($"Tests with area output found: {testCases?.Count ?? 0}");
        }
        
        Console.WriteLine("\nTest completed!");
    }
    
    private static object GetProperty(object obj, string propertyName)
    {
        if (obj == null) return "null";
        var type = obj.GetType();
        var property = type.GetProperty(propertyName);
        return property?.GetValue(obj) ?? "null";
    }
}