using System.Numerics;
using Raylib_CsLo;

namespace T2G_Cookie.Gui;

public interface IInteractable
{
    Rectangle Bounds { get; }
    public bool IsHovered { get; set; }
    public bool IsActivated { get; set; }
    void OnHover();
    void OnUnhover();
    void OnActivated();
    void OnDrag(Vector2 delta);
}

public abstract class Element
{
    protected Func<int> _xResolver = () => 0;
    protected Func<int> _yResolver = () => 0;

    public abstract int Width { get; set; }
    public abstract int Height { get; set; }
    public abstract int WPercent { get; set; }
    public abstract int HPercent { get; set; }

    public int X { get => _xResolver(); set => _xResolver = () => value; }
    public int Y { get => _yResolver(); set => _yResolver = () => value; }

    public Element? Parent { get; set; }
    public List<Element> Children { get; } = new();

    public void AddChild(Element child)
    {
        child.Parent = this;
        Children.Add(child);
    }

    public static List<Element> elements = [];
    public static Frame? HoveredFrame { get; internal set; }

    protected Element() { Register(this); }
    protected Element(Func<int> x, Func<int> y)
    {
        _xResolver = x;
        _yResolver = y;
        Register(this);
    }

    public static void Register(Element element) => elements.Add(element);

    /// <summary>Drops an element from the global list (used when a block is discarded).</summary>
    public static void Unregister(Element element) => elements.Remove(element);

    public static void ClearHover() => HoveredFrame = null;

    public static void UpdateAll()
    {
        foreach (var e in elements.ToArray())
            if (e.Parent == null) e.Update(0, 0);
    }

    public static void DrawAll()
    {
        foreach (var e in elements.ToArray())
            if (e.Parent == null) e.Draw(0, 0);
    }

    public virtual void Update(int offsetX, int offsetY) { }
    public abstract void Draw(int offsetX, int offsetY);
}
