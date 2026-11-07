using Shared;

namespace T2G_Cookie.Main;

internal class Program
{
    static void Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("Nincs fájl megadva.");
            return;
        }

        string fileName = args[0].Equals("-q") 
            ? args[1] 
            : args[0];
        Console.WriteLine(fileName);
        HandleInputFile handler = new HandleInputFile(fileName);
        Console.WriteLine($"Code lines: {handler.Code.Count}");
        Console.WriteLine($"Map: {(handler.Map != null ? $"{handler.Map.GetLength(0)}x{handler.Map.GetLength(1)}" : "null")}");
    }
}