using System.Numerics;
using Raylib_CsLo;

namespace T2G_Cookie.Gui;

public sealed class Toolbox : Frame
{
    sealed class PaletteItem : IInteractable
    {
        public Block Template { get; }
        public Func<Block> Factory { get; }
        public float ContentY { get; }

        public bool IsHovered { get; set; }
        public bool IsActivated { get; set; }

        public Rectangle Bounds => new Rectangle(Theme.CategoryStripWidth + Theme.PalettePadding, (int)ContentY, (int)Template.Width, (int)Template.Height);

        public PaletteItem(Block template, Func<Block> factory, float contentY)
        {
            Template = template;
            Factory = factory;
            ContentY = contentY;
        }

        public void OnHover() { IsHovered = true; Raylib.SetMouseCursor(MouseCursor.MOUSE_CURSOR_POINTING_HAND); }
        public void OnUnhover() { IsHovered = false; Raylib.SetMouseCursor(MouseCursor.MOUSE_CURSOR_ARROW); }
        public void OnActivated() { IsActivated = true; }
        public void OnDrag(Vector2 delta) { }
    }

    const int CategoryRowTop = 16;
    const int CategoryRowHeight = 72;

    readonly List<BlockCategory> _categories;
    readonly List<PaletteItem> _items = new();
    readonly Dictionary<BlockCategory, float> _categoryY = new();
    readonly float _contentHeight;
    float _scroll;
    int _hoveredCategory = -1;
    int _activeCategory = -1;

    internal Block? DragBlock { get; set; }
    Vector2 _dragOffset;

    /// <summary>Where inside the flying block the pointer grabbed it. The release
    /// has to keep this offset or the block jumps and misses its snap target.</summary>
    internal Vector2 DragOffset => _dragOffset;

    /// <summary>Predicate for the singleton hat: false greys "Start clicked" out.</summary>
    internal Func<bool>? StartAvailable { get; set; }

    /// <summary>True while the palette hands out no blocks: a program is running.</summary>
    internal Func<bool>? Locked { get; set; }

    internal Toolbox(Func<int> x, Func<int> y, int wPercent, int hPercent, IEnumerable<BlockCategory> categories)
        : base(x, y, wPercent, hPercent)
    {
        DrawBackground = true;
        Color = Theme.ToolboxBg;
        _categories = categories.ToList();

        float yy = Theme.PalettePadding;
        foreach (BlockCategory category in _categories)
        {
            _categoryY[category] = yy;
            yy += Theme.CategoryTitleHeight;

            foreach (Func<Block> factory in category.Factories)
            {
                Block template = factory();
                // The palette draws its templates itself inside the toolbox scissor.
                // Without a parent the global Element.DrawAll would draw every template
                // again, unclipped, on top of the workspace.
                template.Parent = this;
                _items.Add(new PaletteItem(template, factory, yy));
                yy += template.Height + Theme.PaletteGap;
            }
            yy += Theme.PalettePadding;
        }
        _contentHeight = yy;
    }

    public override void Update(int offsetX, int offsetY)
    {
        base.Update(offsetX, offsetY);

        Width = Theme.ToolboxWidth;
        Height = Parent is Frame p ? p.Height : Raylib.GetScreenHeight();

        int screenX = X + offsetX;
        int screenY = Y + offsetY;
        Vector2 mouse = Raylib.GetMousePosition();
        Vector2 localMouse = mouse - new Vector2(screenX, screenY);

        bool overToolbox = mouse.X >= screenX && mouse.X < screenX + Width && mouse.Y >= screenY && mouse.Y < screenY + Height;

        if (DragBlock != null)
        {
            DragBlock.Layout((int)(mouse.X - _dragOffset.X), (int)(mouse.Y - _dragOffset.Y));
            if (Raylib.IsMouseButtonReleased(MouseButton.MOUSE_BUTTON_LEFT))
                return;
        }

        int hoveredCat = -1;
        if (overToolbox && localMouse.X < Theme.CategoryStripWidth)
        {
            for (int i = 0; i < _categories.Count; i++)
            {
                int top = CategoryRowTop + i * CategoryRowHeight;
                if (localMouse.Y >= top && localMouse.Y < top + CategoryRowHeight)
                {
                    hoveredCat = i;
                    break;
                }
            }
        }
        if (hoveredCat != _hoveredCategory)
            _hoveredCategory = hoveredCat;

        PaletteItem? hovered = null;
        if (overToolbox && localMouse.X >= Theme.CategoryStripWidth)
        {
            foreach (var item in _items)
            {
                int ty = (int)(item.ContentY - _scroll);
                if (localMouse.X >= Theme.CategoryStripWidth + Theme.PalettePadding &&
                    localMouse.X < Theme.CategoryStripWidth + Theme.PalettePadding + item.Template.Width &&
                    localMouse.Y >= ty && localMouse.Y < ty + item.Template.Height)
                {
                    hovered = item;
                    break;
                }
            }
        }

        foreach (var item in _items)
        {
            bool isHover = hovered == item;
            if (isHover && !item.IsHovered) item.OnHover();
            if (!isHover && item.IsHovered) item.OnUnhover();
            item.Template.IsHovered = isHover;
        }

        if (overToolbox)
            _scroll -= Raylib.GetMouseWheelMove() * 40f;
        ClampScroll();
        UpdateActiveCategory();

        foreach (var item in _items)
            item.Template.Layout(Theme.CategoryStripWidth + Theme.PalettePadding, (int)(item.ContentY - _scroll));

        if (overToolbox && Raylib.IsMouseButtonPressed(MouseButton.MOUSE_BUTTON_LEFT))
        {
            if (localMouse.X < Theme.CategoryStripWidth)
            {
                JumpToCategoryAt(localMouse.Y);
            }
            else if (hovered != null && Available(hovered) && Locked?.Invoke() != true)
            {
                DragBlock = hovered.Factory();
                // Keep the in-flight block out of Element.DrawAll: App draws it once,
                // on top of everything else, at absolute screen coordinates.
                DragBlock.Parent = this;
                int absX = screenX + Theme.CategoryStripWidth + Theme.PalettePadding;
                int absY = screenY + (int)(hovered.ContentY - _scroll);
                DragBlock.Layout(absX, absY);
                _dragOffset = mouse - new Vector2(absX, absY);
            }
        }
    }

    public override void Draw(int offsetX, int offsetY)
    {
        int screenX = X + offsetX;
        int screenY = Y + offsetY;
        int h = Parent is Frame p ? p.Height : Raylib.GetScreenHeight();

        if (DrawBackground)
            Raylib.DrawRectangleRounded(new Rectangle(screenX, screenY, Width, Height), 0f, 1, Color);

        Raylib.DrawRectangle(screenX, screenY, Theme.CategoryStripWidth, Height, Theme.StripBg);
        for (int i = 0; i < _categories.Count; i++)
        {
            BlockCategory c = _categories[i];
            int top = screenY + CategoryRowTop + i * CategoryRowHeight;
            bool hovered = i == _hoveredCategory;
            bool active = i == _activeCategory;
            Raylib.DrawCircle(screenX + Theme.CategoryStripWidth / 2, top + 20,
                hovered ? 20f : active ? 18f : 16f, hovered ? Raylib.SKYBLUE : c.Color);
            if (active && !hovered)
                Raylib.DrawCircleLines(screenX + Theme.CategoryStripWidth / 2, top + 20, 20f, c.DarkColor);
            int w = Raylib.MeasureText(c.Name, 12);
            Raylib.DrawText(c.Name, screenX + (Theme.CategoryStripWidth - w) / 2, top + 44, 12,
                hovered ? Raylib.DARKBLUE : active ? c.DarkColor : Theme.TextDark);
        }

        Raylib.BeginScissorMode(screenX + Theme.CategoryStripWidth, screenY, Width - Theme.CategoryStripWidth, Height);
        foreach (BlockCategory category in _categories)
        {
            int ty = (int)(screenY + _categoryY[category] - _scroll);
            int titleX = screenX + Theme.CategoryStripWidth + Theme.PalettePadding;
            Raylib.DrawText(category.Name, titleX, ty + 4, 18, Theme.TextDark);
            if (category.Hint != null)
                Raylib.DrawText(category.Hint, titleX + Raylib.MeasureText(category.Name, 18) + 8, ty + 8, 12, Theme.PlaceholderText);
        }
        foreach (var item in _items)
            item.Template.DrawChain(screenX, screenY);

        // Unavailable items (the Start hat while one already exists) are veiled so the
        // greyed state is visible, not just enforced.
        foreach (var item in _items)
        {
            if (Available(item)) continue;
            int ty = (int)(screenY + item.ContentY - _scroll);
            Raylib.DrawRectangle(screenX + Theme.CategoryStripWidth + Theme.PalettePadding, ty,
                item.Template.Width, item.Template.Height, new Color(255, 255, 255, 165));
        }

        DrawScrollbar(screenX, screenY);
        Raylib.EndScissorMode();

        Raylib.DrawRectangle(screenX + Width - 1, screenY, 1, Height, Theme.Border);
    }

    bool Available(PaletteItem item) => CanCreate(item.Template);

    /// <summary>False greys an item out: the Start hat while one already exists.</summary>
    internal bool CanCreate(Block template)
        => template is not HatBlock || StartAvailable?.Invoke() != false;

    void JumpToCategoryAt(float localMouseY)
    {
        int index = (int)((localMouseY - CategoryRowTop) / CategoryRowHeight);
        if (index < 0 || index >= _categories.Count) return;
        _scroll = _categoryY[_categories[index]] - 4;
        ClampScroll();
    }

    void ClampScroll()
    {
        float max = Math.Max(0f, _contentHeight - Height);
        _scroll = Math.Clamp(_scroll, 0f, max);
    }

    /// <summary>The category whose title is at (or last passed by) the top of the palette.</summary>
    void UpdateActiveCategory()
    {
        _activeCategory = -1;
        for (int i = 0; i < _categories.Count; i++)
            if (_categoryY[_categories[i]] <= _scroll + Theme.CategoryTitleHeight)
                _activeCategory = i;
    }

    void DrawScrollbar(int screenX, int screenY)
    {
        float viewHeight = Height;
        if (_contentHeight <= viewHeight) return;

        int trackX = screenX + Width - 7;
        int trackY = screenY + 6;
        int trackH = Math.Max(8, (int)viewHeight - 12);
        Raylib.DrawRectangleRounded(new Rectangle(trackX, trackY, 4, trackH), 1f, 4, Theme.ScrollTrack);

        float maxScroll = _contentHeight - viewHeight;
        float progress = maxScroll <= 0f ? 0f : _scroll / maxScroll;
        int thumbH = Math.Max(28, (int)(trackH * (viewHeight / _contentHeight)));
        int thumbY = trackY + (int)((trackH - thumbH) * Math.Clamp(progress, 0f, 1f));
        Raylib.DrawRectangleRounded(new Rectangle(trackX, thumbY, 4, thumbH), 1f, 4, Theme.ScrollThumb);
    }
}
