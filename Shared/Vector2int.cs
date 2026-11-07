namespace T2G_Cookie.Shared.Values;

public struct Vector2Int
{
    public int X { get; set; }
    public int Y { get; set; }

    public Vector2Int(int x, int y)
    {
        X = x;
        Y = y;
    }

    public override string ToString()
    {
        return $"<{X} {Y}>";
    }
}