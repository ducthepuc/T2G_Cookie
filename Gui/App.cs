using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using Terminal.Gui.Editor;
using Terminal.Gui.Drawing;
using System.Text;

namespace T2G_Cookie.Gui;

public sealed class App : IDisposable
{
    private readonly IApplication _app;
    private Window? _window;
    private bool _disposed;

    private Editor? codeEditor = null;
    private Editor? mapEditor = null;
    private Editor? map = null;

    public App()
    {
        _app = Application.Create();
        _app.Init();
    }

    public void Run()
    {
        _window = new Window { Title = "RoboMiner - T2G" };

        Tabs editorTabs = new()
        {
            X = 0,
            Y = 0,
            Width = Dim.Percent(50),
            Height = Dim.Fill(),
        };

        View codeTab = new()
        {
            Title = "Script",
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            CanFocus = true
        };

        codeEditor = new()
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            WordWrap = true
        };
        codeTab.Add(codeEditor);

        View mapTab = new()
        {
            Title = "Map",
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            CanFocus = true
        };

        mapEditor = new()
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            WordWrap = true
        };
        mapTab.Add(mapEditor);

        editorTabs.Add(codeTab);
        editorTabs.Add(mapTab);
        editorTabs.Value = codeTab;

        FrameView mapFrame = new()
        {
            Title = "RobotView",
            X = Pos.Right(editorTabs),
            Y = 0,
            Width = Dim.Percent(50),
            Height = Dim.Percent(60)
        };

        map = new()
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            ReadOnly = true,
            WordWrap = true,
        };
        mapFrame.Add(map);

        FrameView controlsFrame = new()
        {
            X = Pos.Right(editorTabs),
            Y = Pos.Bottom(mapFrame),
            Width = Dim.Percent(50),
            Height = Dim.Percent(20)
        };

        Button upBtn = new()
        {
            Text = "UP",
            X = Pos.Center(),
            Y = Pos.Percent(10),
            Enabled = false,
        };
        controlsFrame.Add(upBtn);

        Button downBtn = new()
        {
            Text = "DOWN",
            X = Pos.Center(),
            Y = Pos.Bottom(upBtn),
            Enabled = false,
        };
        controlsFrame.Add(downBtn);

        Button leftBtn = new()
        {
            Text = "LEFT",
            X = Pos.Left(downBtn) - 10,
            Y = Pos.Bottom(upBtn),
            Enabled = false,
        };
        controlsFrame.Add(leftBtn);

        Button rightBtn = new()
        {
            Text = "RIGHT",
            X = Pos.Right(downBtn) + 1,
            Y = Pos.Bottom(upBtn),
            Enabled = false,
        };
        controlsFrame.Add(rightBtn);

        CheckBox robotControl = new()
        {
            Text = "Control robot",
            X = Pos.Right(editorTabs) + 2,
            Y = Pos.Bottom(controlsFrame),
            Value = CheckState.UnChecked
        };

        Button sendBtn = new()
        {
            Text = "Send Code",
            X = Pos.Right(editorTabs) + 2,
            Y = Pos.AnchorEnd()
        };
        Editor ActiveEditor() =>
            editorTabs.Value as Editor;

        sendBtn.Accepting += (_, e) =>
        {
            Editor current = ActiveEditor();
            map.Text += current.Text + Environment.NewLine;
            current.Text = string.Empty;
            current.SetFocus();
            e.Handled = true;
        };

        Button clearBtn = new()
        {
            Text = "Clear Editor",
            X = Pos.Right(sendBtn) + 2,
            Y = Pos.Top(sendBtn)
        };
        clearBtn.Accepting += (_, e) =>
        {
            map.Text = string.Empty;
            e.Handled = true;
        };

        Button quitBtn = new()
        {
            Text = "Quit RoboMiner",
            X = Pos.Right(clearBtn) + 2,
            Y = Pos.Top(sendBtn)
        };
        quitBtn.Accepting += (_, e) =>
        {
            _app.RequestStop();
            e.Handled = true;
        };

        void SetRobotControlsEnabled(bool enabled)
        {
            upBtn.Enabled = enabled;
            downBtn.Enabled = enabled;
            leftBtn.Enabled = enabled;
            rightBtn.Enabled = enabled;
            sendBtn.Enabled = !enabled;
            clearBtn.Enabled = !enabled;
        }

        robotControl.ValueChanged += (_, _) =>
        {
            SetRobotControlsEnabled(robotControl.Value == CheckState.Checked);
        };
        SetRobotControlsEnabled(robotControl.Value == CheckState.Checked);

        _window.Add(editorTabs, mapFrame, controlsFrame, robotControl, sendBtn, clearBtn, quitBtn);
        _app.Run(_window);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _window?.Dispose();
        _app.Dispose();
    }

    public void SetScriptContent(List<string> code)
    {
        codeEditor!.Text = String.Join("\n", code);
    }

    public void SetMapContent(char[,] map)
    {
        StringBuilder sb = new StringBuilder();
        int rows = map.GetLength(0);
        int cols = map.GetLength(1);

        for (int i = 0; i < rows; i++)
        {
            for (int j = 0; j < cols; j++)
            {
                sb.Append(map[i, j]);
            }
            if (i < rows - 1)
            {
                sb.AppendLine();
            }
        }

        string text = sb.ToString();
        mapEditor!.Text = text;
    }
}

internal class Program
{
    static void Main()
    {
        using App app = new();
        app.Run();
    }
}