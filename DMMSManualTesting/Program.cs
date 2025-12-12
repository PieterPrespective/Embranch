using System;
using System.Threading.Tasks;

namespace DMMSManualTesting;

public class Program
{
    public static async Task Main(string[] args)
    {
        Console.WriteLine("DMMS Testing Console");
        Console.WriteLine("===================");
        Console.WriteLine();
        Console.WriteLine("Available tests:");
        Console.WriteLine("1. Python.NET Hang Test");
        Console.WriteLine("2. Manual Credential Test");
        Console.WriteLine("3. VM RAG Test (DoltHub Integration)");
        Console.WriteLine("4. VM RAG Test - Existing Database");
        Console.WriteLine("5. VM RAG Test - Simple (Native Dolt Login)");
        Console.WriteLine();
        Console.Write("Select test (1-5) or press Enter for credential test: ");
        
        var choice = Console.ReadLine()?.Trim();
        
        switch (choice)
        {
            case "1":
                await PythonNetHangTest.Run();
                break;
            case "2":
            case "":
                await ManualCredentialTest.Run();
                break;
            case "3":
                await VMRAGTest.Run();
                break;
            case "4":
                await VMRAGTestExisting.Run();
                break;
            case "5":
                await VMRAGTestSimple.Run();
                break;
            default:
                await ManualCredentialTest.Run();
                break;
        }
    }
}