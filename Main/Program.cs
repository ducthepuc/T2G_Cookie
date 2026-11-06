using T2G_Cookie.Gui;
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
        
        using App app = new();
        app.SetMapContent(handler.Map);
        app.SetScriptContent(handler.Code);
        app.Run();
    }
}

public class HandleInputFile
{
    public List<string> Code { get; private set; }
    public char[,]? Map { get; set; }
    public HandleInputFile(string fileName)
    {
        Code = [];
        ReadFile(fileName);
    }
    public char[,]? ReadFile(string file)
    {
        string[] mapIdentifiers = ["#", ".", "@", ">", "<", "^", "v"];
        int currentRow = 0;
        using FileStream fs = new(file, FileMode.Open);
        using StreamReader sr = new(fs);
        string[] nums = sr.ReadLine().Split(" ");
        int rows = int.Parse(nums[0]);
        int cols = int.Parse(nums[1]);
        Map = new char[rows,cols];
        while (sr.EndOfStream == false)
        {
            string line = sr.ReadLine();
            if (mapIdentifiers.Any(line.StartsWith))
            {
                if (currentRow < Map.GetLength(0))
                {
                    for (int col = 0; col < line.Length && col < Map.GetLength(1); col++)
                    {
                        Map[currentRow, col] = line[col];
                    }
                    currentRow++;
                }
            }
            else
            {
                Code.Add(line);
            }
        }
        return Map;
    }
}