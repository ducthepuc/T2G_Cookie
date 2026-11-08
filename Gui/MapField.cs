using System.Numerics;
using Raylib_CsLo;
using T2G_Cookie.Shared;
using T2G_Cookie.Shared.Values;

namespace T2G_Cookie.Gui;

/// <summary>
/// The map panel under the print-output field. It shares the output's width - the
/// x resolver anchors to the output's left edge and the size is the same Dim
/// percent - and stays square, so the 15x15 grid is one uniform tile size. The
/// host loads the <see cref="MapHandler"/> the input file produced with
/// <see cref="SetMap"/>; every cell is drawn from the handler's parsed elements:
/// a small grey circle for an empty cell, a small yellow square for gold, a red
/// square filling the whole cell for a wall, and a blue triangle on the robot's
/// cell rotated to its starting direction.
/// </summary>
sealed class MapField : Frame
{
    /// <summary>The grid is always 15x15 - the biggest map the game accepts.</summary>
    public const int Cells = 15;

    MapHandler? _map;

    public MapField(Func<int> x, Func<int> y, int wPercent)
        : base(x, y, wPercent, 0)
    {
        DrawBackground = true;
        Color = Raylib.WHITE;
    }

    /// <summary>The loaded handler, or null when no file was given.</summary>
    public MapHandler? Map => _map;

    /// <summary>Loads the map the host parsed from the input file.</summary>
    public void SetMap(MapHandler? map) => _map = map;

    public override void Draw(int offsetX, int offsetY)
    {
        // The width scales with the screen through Dim (the output's percent); the
        // panel is square, so Cells rows of Width/Cells land on square tiles.
        Width = Dim.GetWSize(WPercent);
        Height = Width;
        (int drawX, int drawY) = ResolvePosition(offsetX, offsetY);
        // Never reach below the window or swallow the robot-control stack under the
        // panel: the tile size follows the clamped height.
        Height = Math.Max(16, Math.Min(Height,
            Raylib.GetScreenHeight() - drawY - Theme.RobotStackBelow));

        base.Draw(offsetX, offsetY); // square background, no children to clip

        (drawX, drawY) = ResolvePosition(offsetX, offsetY);
        Raylib.DrawRectangleLines(drawX, drawY, Width, Height, Theme.Border);

        if (_map == null)
        {
            Raylib.DrawText("No map loaded",
                drawX + 10, drawY + (Height - OutputField.TextSize) / 2,
                OutputField.TextSize, Theme.PlaceholderText);
            return;
        }

        int tile = Math.Max(1, Math.Min(Width, Height) / Cells);
        int grid = tile * Cells;
        int originX = drawX + (Width - grid) / 2;
        int originY = drawY + (Height - grid) / 2;

        for (int i = 0; i <= Cells; i++)
        {
            Raylib.DrawLine(originX + i * tile, originY,
                originX + i * tile, originY + grid, Theme.MapGridLine);
            Raylib.DrawLine(originX, originY + i * tile,
                originX + grid, originY + i * tile, Theme.MapGridLine);
        }

        // Cells past the map stay plain grid: the handler knows its own rows/columns,
        // and a map larger than the grid can't happen (Main rejects it before launch).
        Vector2Int? robot = _map.IdentifyRobot(out Direction? facing);
        for (int row = 0; row < _map.Rows && row < Cells; row++)
        {
            for (int col = 0; col < _map.Cols && col < Cells; col++)
            {
                int x = originX + col * tile;
                int y = originY + row * tile;

                MapHandler.MapElement element = _map.ElementAt(row, col);
                bool robotHere = robot is { } position && position.X == col && position.Y == row;

                if (!robotHere)
                    DrawCell(element, x, y, tile);
                else if (element == MapHandler.MapElement.Gold)
                    DrawFloatingGold(x, y, tile); // gold the robot stands on stays visible
                else if (element == MapHandler.MapElement.Wall)
                    Raylib.DrawRectangle(x, y, tile, tile, Theme.MapWall); // mined underfoot

                if (robotHere)
                    DrawRobot(x + tile / 2, y + tile / 2, tile, facing!.Value);
            }
        }
    }

    /// <summary>The cell's gold, shrunk and parked in the top-left corner so the
    /// robot standing on the cell does not hide it.</summary>
    static void DrawFloatingGold(int x, int y, int tile)
    {
        int small = Math.Max(2, tile / 6);
        Raylib.DrawRectangle(x + 2, y + 2, small, small, Theme.MapGold);
    }

    static void DrawCell(MapHandler.MapElement element, int x, int y, int tile)
    {
        int centerX = x + tile / 2;
        int centerY = y + tile / 2;

        switch (element)
        {
            case MapHandler.MapElement.Empty:
                Raylib.DrawCircle(centerX, centerY, Math.Max(1, tile / 6), Theme.MapEmptyDot);
                break;
            case MapHandler.MapElement.Gold:
                int box = Math.Max(2, tile / 3);
                Raylib.DrawRectangle(centerX - box / 2, centerY - box / 2, box, box, Theme.MapGold);
                break;
            case MapHandler.MapElement.Wall:
                Raylib.DrawRectangle(x, y, tile, tile, Theme.MapWall);
                break;
        }
    }

    static void DrawRobot(int centerX, int centerY, int tile, Direction facing)
    {
        float dx = facing.ModX;
        float dy = facing.ModY;

        float radius = tile * 0.34f;
        var tip = new Vector2(centerX + dx * radius, centerY + dy * radius);
        var back = new Vector2(centerX - dx * radius * 0.5f, centerY - dy * radius * 0.5f);
        var side = new Vector2(-dy, dx);
        var first = new Vector2(back.X + side.X * radius * 0.8f, back.Y + side.Y * radius * 0.8f);
        var second = new Vector2(back.X - side.X * radius * 0.8f, back.Y - side.Y * radius * 0.8f);

        // Keep the on-screen counter-clockwise order the basic-shapes example uses,
        // so the triangle rasterises whichever way it points.
        float cross = (first.X - tip.X) * (second.Y - tip.Y) -
                      (first.Y - tip.Y) * (second.X - tip.X);
        if (cross > 0) (first, second) = (second, first);

        Raylib.DrawTriangle(tip, first, second, Theme.MapRobot);
    }
}
