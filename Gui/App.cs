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

        Image appimage = Raylib.LoadImage(Path.Combine(AppContext.BaseDirectory, "assets", "icon.png"));
        
        

        Raylib.SetWindowIcon(appimage);
        Raylib.UnloadImage(appimage);

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
                if (OverWorkspace(Workspace, mouse))
                {
                    // Keep the grab offset: the release must land the block where the
                    // drag drew it, or FindSnap misses the connection it was aimed at.
                    Workspace!.AddRootBlock(dragBlock, mouse, Toolbox.DragOffset);
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

            // The block flying out of the toolbox: show the snap the release would
            // take, then draw the block itself, in absolute screen coordinates.
            if (Toolbox?.DragBlock is { } dragging)
            {
                Vector2 mouse = Raylib.GetMousePosition();
                SnapTarget? preview = OverWorkspace(Workspace, mouse)
                    ? Workspace!.FindSnapForScreen(dragging, mouse, Toolbox.DragOffset)
                    : null;
                dragging.Draw(0, 0);
                // Drawn after the block, the order Workspace.Draw uses too, so the
                // edge bar stays visible where it overlaps the dragged block.
                preview?.Draw(Workspace!.ScreenOrigin.X, Workspace.ScreenOrigin.Y);
            }

            Raylib.EndDrawing();
        }
        Raylib.CloseWindow();
    }

    /// <summary>Shared by the toolbox release and its drag preview: the workspace's
    /// screen rectangle, the one place a flying block may land.</summary>
    static bool OverWorkspace(Workspace? workspace, Vector2 mouse) =>
        workspace != null &&
        mouse.X >= workspace.X && mouse.X < workspace.X + workspace.Width &&
        mouse.Y >= workspace.Y && mouse.Y < workspace.Y + workspace.Height;
}
