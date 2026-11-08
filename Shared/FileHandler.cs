using System.IO;
using System.Threading;

namespace Shared;

public class HandleInputFile
{
    private readonly string _fileName;

    /// <summary>Characters a line may start with for it to be map content, not code.</summary>
    static readonly string[] MapIdentifiers = ["#", ".", "@", ">", "<", "^", "v"];

    // The file exactly as it was read. A save writes the map part back verbatim from
    // here and only replaces the code below it, so the in-memory map - a robot walked
    // to a new cell, gold mined - never leaks into the file.
    readonly List<string> _rawLines = [];
    int _firstCodeLine = -1; // index of the first code line in _rawLines, -1 = no code

    public List<string> Code { get; private set; }
    public char[,]? Map { get; private set; }

    /// <summary>The file this handler reads from and writes back to.</summary>
    public string FileName => _fileName;

    public HandleInputFile(string fileName)
    {
        _fileName = fileName;
        Code = [];
        ReadFile(fileName);
    }
    public char[,]? ReadFile(string file)
    {
        int currentRow = 0;
        _rawLines.Clear();
        Code.Clear();
        _firstCodeLine = -1;
        using FileStream fs = new(file, FileMode.Open, FileAccess.Read);
        using StreamReader sr = new(fs);
        
        string? headerLine = sr.ReadLine();
        _rawLines.Add(headerLine ?? "");
        string[] nums = headerLine!.Split(" ");
        int rows = int.Parse(nums[0]);
        int cols = int.Parse(nums[1]);
        Map = new char[rows,cols];
        
        for (int r = 0; r < rows && !sr.EndOfStream; r++)
        {
            string? mapLine = sr.ReadLine();
            if (mapLine == null) break;
            _rawLines.Add(mapLine);
            
            for (int c = 0; c < mapLine.Length && c < cols; c++)
            {
                Map[r, c] = mapLine[c];
            }
        }
        
        while (!sr.EndOfStream)
        {
            string? line = sr.ReadLine();
            if (line == null) break;
            _rawLines.Add(line);
            if (MapIdentifiers.Any(line.StartsWith))
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
                if (_firstCodeLine < 0) _firstCodeLine = _rawLines.Count - 1;
                Code.Add(line);
            }
        }
        return Map;
    }

    /// <summary>
    /// Writes the file back as the map part exactly as it was read - every line up to
    /// the original code, then any map lines the file kept below the code - followed
    /// by the new code. Only the code section is replaced: the in-memory map never
    /// reaches the file. Blank lines are dropped; they would be read back as code.
    /// </summary>
    public void Write(string source)
    {
        int codeStart = _firstCodeLine < 0 ? _rawLines.Count : _firstCodeLine;
        var lines = new List<string>();

        for (int i = 0; i < codeStart; i++)
            if (_rawLines[i].Length > 0) lines.Add(_rawLines[i]);

        // The rendered source may use \r\n; strip the CR so lines read back clean.
        foreach (string line in source.Split('\n'))
        {
            string clean = line.TrimEnd('\r');
            if (clean.Length > 0) lines.Add(clean);
        }

        // Map lines below the original code (a trailing corrected map block) stay
        // below the new code; the old code lines are dropped with the rest.
        for (int i = codeStart; i < _rawLines.Count; i++)
        {
            string line = _rawLines[i];
            if (line.Length > 0 && MapIdentifiers.Any(line.StartsWith)) lines.Add(line);
        }

        File.WriteAllLines(_fileName, lines);
    }
}
public class HandleOutputFile : IDisposable
{
    private readonly StreamWriter _writer;
    private readonly object _lock = new();
    private bool _finished;

    public HandleOutputFile(string outputPath)
    {
        var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
        _writer = new StreamWriter(fs) { AutoFlush = true, NewLine = "\n" };
    }

    public void Log(string line)
    {
        lock (_lock)
        {
            if (_finished) return;
            _writer.WriteLine(line);
        }
    }

    public void Finish(bool exceeded)
    {
        lock (_lock)
        {
            if (_finished) return;
            _writer.WriteLine(exceeded ? "INSTRUCTION_COUNT_LIMIT_EXCEEDED" : "EOR");
            _finished = true;
            _writer.Dispose();   // run is over, release the file
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _finished = true;
            _writer.Dispose();   // safe to call twice
        }
    }
}
