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
                int screenX = X + offsetX;
                int screenY = Y + offsetY;

                int containerW = Parent is Frame p ? p.Width : Raylib.GetScreenWidth();
                int containerH = Parent is Frame p2 ? p2.Height : Raylib.GetScreenHeight();
                int w = containerW * WPercent / 100;
                int h = containerH * HPercent / 100;

                // Publish the size during Update as well: children resolve their position
                // against it before the first Draw has ever run.
                Width = w;
                Height = h;

                int drawX = HAnchor == Anchor.End ? containerW - w : screenX;
                int drawY = VAnchor == Anchor.End ? containerH - h : screenY;

                Vector2 mouse = Raylib.GetMousePosition();
                bool over = mouse.X >= drawX && mouse.X < drawX + w && mouse.Y >= drawY && mouse.Y < drawY + h;

                if (over) HoveredFrame = this;

                foreach (var child in Children)
                        if (child is Frame cf)
                                cf.Update(drawX - ScrollX, drawY - ScrollY);
        }

        public override void Draw(int offsetX, int offsetY)
        {
                int screenW = Raylib.GetScreenWidth();
                int screenH = Raylib.GetScreenHeight();

                int screenX = X + offsetX;
                int screenY = Y + offsetY;

                int containerW = Parent is Frame p ? p.Width : screenW;
                int containerH = Parent is Frame p2 ? p2.Height : screenH;
                Width = containerW * WPercent / 100;
                Height = containerH * HPercent / 100;

                int drawX = HAnchor == Anchor.End ? containerW - Width : screenX;
                int drawY = VAnchor == Anchor.End ? containerH - Height : screenY;

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
                        int childW = child.WPercent > 0 ? containerW * child.WPercent / 100 : child.Width;
                        int childH = child.HPercent > 0 ? containerH * child.HPercent / 100 : child.Height;
                        cw = Math.Max(cw, child.X + childW);
                        ch = Math.Max(ch, child.Y + childH);
                        if (child is Frame cf)
                                cf.ComputeContentExtent(childW, childH, ref cw, ref ch);
                }
        }
}
