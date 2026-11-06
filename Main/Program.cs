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
        using FileStream fs = new(file, FileMode.Open, FileAccess.Read);
        using StreamReader sr = new(fs);
        string[] nums = sr.ReadLine().Split(" ");
        int rows = int.Parse(nums[0]);
        int cols = int.Parse(nums[1]);
        Map = new char[rows,cols];
        
        for (int r = 0; r < rows && !sr.EndOfStream; r++)
        {
            string? mapLine = sr.ReadLine();
            if (mapLine == null) break;

            for (int c = 0; c < mapLine.Length && c < cols; c++)
            {
                Map[r, c] = mapLine[c];
            }
        }
        
        while (!sr.EndOfStream)
        {
            string? line = sr.ReadLine();
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
        for (int i = 0; i < Code.Count; i++)
        {
            Console.WriteLine(Code[i]);
        }
        return Map;
    }
}