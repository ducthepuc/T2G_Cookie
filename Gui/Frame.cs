using System.Numerics;
using Raylib_CsLo;

namespace T2G_Cookie.Gui;

public enum Anchor
{
        Start,
        End
}

public class Frame : Element
{
        public override int Width { get; set; }
        public override int Height { get; set; }
        public override int WPercent { get; set; }
        public override int HPercent { get; set; }

        public Anchor HAnchor { get; set; } = Anchor.Start;
        public Anchor VAnchor { get; set; } = Anchor.Start;

        public int ScrollX { get; set; }
        public int ScrollY { get; set; }

        public Color Color { get; set; } = Raylib.GRAY;
        public bool DrawBackground { get; set; } = false;

        public Frame(Func<int> x, Func<int> y, int wPercent, int hPercent, Anchor hAnchor = Anchor.Start, Anchor vAnchor = Anchor.Start) : base(x, y)
        {
                WPercent = wPercent;
                HPercent = hPercent;
                HAnchor = hAnchor;
                VAnchor = vAnchor;
        }
        public override void Update(int offsetX, int offsetY)
        {
                // Size responds to the screen via Dim (percent of screen width/height).
                Width = WPercent > 0 ? Dim.GetWSize(WPercent) : Width;
                Height = HPercent > 0 ? Dim.GetHSize(HPercent) : Height;

                (int drawX, int drawY) = ResolvePosition(offsetX, offsetY);

                Vector2 mouse = Raylib.GetMousePosition();
                bool over = mouse.X >= drawX && mouse.X < drawX + Width && mouse.Y >= drawY && mouse.Y < drawY + Height;

                if (over) HoveredFrame = this;

                foreach (var child in Children)
                        if (child is Frame cf)
                                cf.Update(drawX - ScrollX, drawY - ScrollY);
        }

        /// <summary>
        /// Where this frame's top-left lands on screen. Start follows the offset chain
        /// it was handed; End anchors to the container's far edge <i>within</i> that
        /// chain, so a right-aligned child of a right-shifted parent still ends up
        /// where the parent draws (the old End branch dropped the offset and pulled
        /// such children back toward the window's origin).
        /// </summary>
        protected (int X, int Y) ResolvePosition(int offsetX, int offsetY)
        {
                int containerW = Parent is Frame p ? p.Width : Raylib.GetScreenWidth();
                int containerH = Parent is Frame p2 ? p2.Height : Raylib.GetScreenHeight();

                int x = HAnchor == Anchor.End ? offsetX + containerW - Width : X + offsetX;
                int y = VAnchor == Anchor.End ? offsetY + containerH - Height : Y + offsetY;
                return (x, y);
        }

        public override void Draw(int offsetX, int offsetY)
        {
                // Size responds to the screen via Dim (percent of screen width/height).
                Width = WPercent > 0 ? Dim.GetWSize(WPercent) : Width;
                Height = HPercent > 0 ? Dim.GetHSize(HPercent) : Height;

                (int drawX, int drawY) = ResolvePosition(offsetX, offsetY);

                int contentW = Width;
                int contentH = Height;
                ComputeContentExtent(Width, Height, ref contentW, ref contentH);

                int maxScrollX = Math.Max(0, contentW - Width);
                int maxScrollY = Math.Max(0, contentH - Height);
                ScrollX = Math.Clamp(ScrollX, 0, maxScrollX);
                ScrollY = Math.Clamp(ScrollY, 0, maxScrollY);

                if (DrawBackground)
                        Raylib.DrawRectangleRounded(new Rectangle(drawX, drawY, Width, Height), 0f, 1, Color);

                Raylib.BeginScissorMode(drawX, drawY, Width, Height);
                foreach (var child in Children)
                        child.Draw(drawX - ScrollX, drawY - ScrollY);
                Raylib.EndScissorMode();
        }

        private void ComputeContentExtent(int containerW, int containerH, ref int cw, ref int ch)
        {
                foreach (var child in Children)
                {
                        int childW = child.WPercent > 0 ? Dim.GetWSize(child.WPercent) : child.Width;
                        int childH = child.HPercent > 0 ? Dim.GetHSize(child.HPercent) : child.Height;
                        cw = Math.Max(cw, child.X + childW);
                        ch = Math.Max(ch, child.Y + childH);
                        if (child is Frame cf)
                                cf.ComputeContentExtent(childW, childH, ref cw, ref ch);
                }
        }
}
