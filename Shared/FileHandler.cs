using System.IO;
using System.Threading;

namespace Shared;

public class HandleInputFile
{
    public List<string> Code { get; private set; }
    public char[,]? Map { get; private set; }
    
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
        return Map;
    }
}

public class HandleOutputFile : IDisposable
{
    private readonly StreamWriter _writer;
    private readonly object _lock = new();
    private bool _finished;

    public HandleOutputFile(string outputPath)
    {
        var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
        _writer = new StreamWriter(fs);
        {
            _writer.AutoFlush = true;
        }
    }

    public void Log(string line)
    {
        lock (_lock)
        {
            if (_finished) return;
            _writer.WriteLine(line);
        }
    }

    public void Finish()
    {
        lock (_lock)
        {
            if (_finished) return;
            _writer.WriteLine("EOR");
            _finished = true;
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _writer.Dispose();
        }
    }
}
