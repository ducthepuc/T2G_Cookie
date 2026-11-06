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
        HandleInputFile.PrintMap();
    }
}

public class HandleInputFile
{
    public static List<string> Code { get; private set; }
    private static char[,]? Map { get; set; }
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
        for (int i = 0; i < Code.Count; i++)
        {
            Console.WriteLine(Code[i]);
        }
        return Map;
    }
    
    public static void PrintMap()
    {
        if (Map is null)
        {
            Console.WriteLine("Map is not loaded.");
            return;
        }

        int rows = Map.GetLength(0);
        int cols = Map.GetLength(1);

        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                char ch = Map[r, c];
                Console.Write(ch == '\0' ? ' ' : ch);
            }
            Console.WriteLine();
        }
    }
}