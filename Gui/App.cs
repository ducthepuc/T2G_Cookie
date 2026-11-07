using System.Numerics;
using Raylib_CsLo;

namespace T2G_Cookie.Gui;

public class App
{
    public const int ScreenW = 1000;
    public const int ScreenH = 650;

    public Toolbox? Toolbox { get; set; }
    public Workspace? Workspace { get; set; }

    public App()
    {
        Raylib.SetConfigFlags(ConfigFlags.FLAG_WINDOW_RESIZABLE);
        Raylib.InitWindow(ScreenW, ScreenH, "RoboMiner");
        RayGui.GuiLoadStyleDefault();
        Raylib.SetTargetFPS(120);
    }

    public void Run() => Draw();

    private void Draw()
    {
        while (!Raylib.WindowShouldClose())
        {
            Raylib.BeginDrawing();
            Raylib.ClearBackground(Raylib.WHITE);
            Element.ClearHover();
            Element.UpdateAll();

            // When a block dragged from the toolbox is released:
            //  - over the workspace → add as a new root stack
            //  - over the toolbox   → delete it
            if (Toolbox?.DragBlock is { } dragBlock &&
                Raylib.IsMouseButtonReleased(MouseButton.MOUSE_BUTTON_LEFT))
            {
                Vector2 mouse = Raylib.GetMousePosition();
                bool overWorkspace = Workspace != null &&
                    mouse.X >= Workspace.X && mouse.X < Workspace.X + Workspace.Width &&
                    mouse.Y >= Workspace.Y && mouse.Y < Workspace.Y + Workspace.Height;
                if (overWorkspace)
                {
                    Workspace!.AddRootBlock(dragBlock, mouse);
                }
                else
                {
                    // dropped outside the workspace: discard. It has to leave the
                    // global element list too, or it keeps being drawn forever as a ghost.
                    dragBlock.UnregisterTree();
                }
                Toolbox.DragBlock = null;
            }

            float wheel = Raylib.GetMouseWheelMove();
            if (wheel != 0f && Element.HoveredFrame is Frame f)
            {
                bool shift = Raylib.IsKeyDown(KeyboardKey.KEY_LEFT_SHIFT) || Raylib.IsKeyDown(KeyboardKey.KEY_RIGHT_SHIFT);
                int delta = (int)(wheel * 20f);
                if (shift) f.ScrollX -= delta; else f.ScrollY -= delta;
            }

            Element.DrawAll();

            // Draw the block currently being dragged from the toolbox
            // (it lives in absolute screen coordinates, so no extra offset).
            if (Toolbox?.DragBlock is { } dragging)
                dragging.Draw(0, 0);

            Raylib.EndDrawing();
        }
        Raylib.CloseWindow();
    }
}
