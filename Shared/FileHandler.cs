namespace Shared;

public class CodeBlock
{
    public string? Command { get; set; }
    public int IndentLevel { get; set; }
    public CodeBlock? Next { get; set; }
    public List<CodeBlock> Children { get; } = new();
    
    public CodeBlock(string? command, int  indentLevel = 0)
    {
        Command = command;
        IndentLevel = indentLevel;
    }
}

public class CodeBlockList
{
    public CodeBlock? Head { get; set; }
    public CodeBlock? Tail { get; set; }
    public int Count { get; private set; }

    public void AddLast(CodeBlock block)
    {
        if (Head == null)
        {
            Head = Tail = block;
        }
        else
        {
            Tail!.Next = block;
            Tail = block;
        }
        Count++;
    }

    public IEnumerable<CodeBlock> Flatten()
    {
        var current = Head;
        while (current != null)
        {
            yield return current;
            foreach (var child in FlattenRecursive(current.Children))
            {
                yield return child;
            }
            current = current.Next;
        }
    }

    private static IEnumerable<CodeBlock> FlattenRecursive(List<CodeBlock> children)
    {
        foreach (var child in children)
        {
            yield return child;
            foreach (var grandchild in FlattenRecursive(child.Children))
            {
                yield return grandchild;
            }
        }
    }
}

public class HandleInputFile
{
    public CodeBlockList Code { get; } = new();
    public char[,]? Map { get; private set; }
    
    public HandleInputFile(string fileName)
    {
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
        
        var indentStack = new Stack<(CodeBlock block, int level)>();
        
        while (!sr.EndOfStream)
        {
            string? line = sr.ReadLine();
            if (line == null) break;
            
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
                continue;
            }

            int indent = 0;
            while (indent < line.Length && line[indent] == ' ') indent++;
            string trim = line.Trim();
            if (string.IsNullOrEmpty(trim)) continue;

            int indentLevel = indent / 4;

            while (indentStack.Count > 0 && indentStack.Peek().level >= indentLevel)
            {
                indentStack.Pop();
            }
            
            var block = new CodeBlock(trim, indentLevel);

            if (indentStack.Count == 0)
            {
                Code.AddLast(block);
            }
            else
            {
                indentStack.Peek().block.Children.Add(block);
            }
            
            indentStack.Push((block, indentLevel));
        }
        return Map;
    }
}
