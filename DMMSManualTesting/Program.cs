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
        Console.WriteLine("1. VM RAG Test - Simple (Native Dolt Login)");
        Console.WriteLine();
        Console.Write("Select test (1-1) or press Enter for credential test: ");
        
        var choice = Console.ReadLine()?.Trim();
        
        switch (choice)
        {
            case "1":
                await VMRAGTestSimple.Run();
                break;
            
        }
    }
}