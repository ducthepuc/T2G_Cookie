namespace T2G_Cookie.Shared.Values;

public readonly record struct Direction(int ModX, int ModY)
{
    public static readonly Direction Up    = new(0, -1);
    public static readonly Direction Down  = new(0, 1);
    public static readonly Direction Left  = new(-1, 0);
    public static readonly Direction Right = new(1, 0);

    public Direction TurnLeft() => new(ModY, -ModX);
}
