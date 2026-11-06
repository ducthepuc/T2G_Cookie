using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using Terminal.Gui.Editor;
using Terminal.Gui.Drawing;
using System.Text;

namespace T2G_Cookie.Gui;

internal class Program
{
    static void Main(string[] args) {}
}

public sealed class App : IDisposable
{
    private readonly IApplication _app;
    private Window? _window;
    private bool _disposed;

    private Editor? codeEditor = null;
    private Editor? mapEditor = null;
    private Label? map = null;

    private List<string>? _pendingScript;
    private char[,]? _pendingMap;
    private string? _pendingMapText;

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
        if (_pendingScript != null)
        {
            codeEditor.Document = new(string.Join(Environment.NewLine, _pendingScript));
            _pendingScript = null;
        }
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
        if (_pendingMap is { } pendingMap)
        {
            StringBuilder sb = new();
            for (int i = 0; i < pendingMap.GetLength(0); i++)
            {
                for (int j = 0; j < pendingMap.GetLength(1); j++)
                    sb.Append(pendingMap[i, j]);
                if (i < pendingMap.GetLength(0) - 1)
                    sb.AppendLine();
            }
            mapEditor.Document = new(sb.ToString());
            _pendingMapText = sb.ToString();
            _pendingMap = null;
        }
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
            Text = "Bibibubu",
            X = Pos.Center(),
            Y = Pos.Center(),
        };
        mapFrame.Add(map);

        if (_pendingMapText is { } pendingMapText)
        {
            DrawMap(pendingMapText);
            _pendingMapText = null;
        }

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
            Text = "Execute Script",
            X = Pos.Right(editorTabs) + 2,
            Y = Pos.AnchorEnd()
        };
        Editor ActiveEditor() =>
            editorTabs.Value as Editor;

        sendBtn.Accepting += (_, e) =>
        {
            codeEditor.Text = string.Empty;
            codeEditor.SetFocus();
            e.Handled = true;
        };

        Button clearBtn = new()
        {
            Text = "Set Map",
            X = Pos.Right(sendBtn) + 2,
            Y = Pos.Top(sendBtn)
        };
        clearBtn.Accepting += (_, e) =>
        {
            DrawMap(mapEditor.Text);
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
        if (codeEditor != null)
        {
            codeEditor.Document = new(string.Join(Environment.NewLine, code));
        }
        else
        {
            _pendingScript = code;
        }
    }

    public void SetMapContent(char[,] map)
    {
        if (mapEditor != null)
        {
            StringBuilder sb = new();
            for (int i = 0; i < map.GetLength(0); i++)
            {
                for (int j = 0; j < map.GetLength(1); j++)
                    sb.Append(map[i, j]);
                if (i < map.GetLength(0) - 1)
                    sb.AppendLine();
            }
            mapEditor.Document = new(sb.ToString());
            DrawMap(sb.ToString());
        }
        else
        {
            _pendingMap = map;
        }
    }

    private void DrawMap(string mapData)
    {
        if (map == null) return;
        string newMap = "";
        Dictionary<char, char> cells = new()
        {
            {'#', '■'},
            {'.', '•'},
            {'@', '⣿'},
            {'>', '⇨'},
            {'<', '⇦'},
            {'^', '⇧'},
            {'v', '⇩'}
        };

        foreach (char c in mapData)
        {
            if (cells.TryGetValue(c, out var replacement))
                newMap += replacement;
            else if (c == '\n' || c == '\r')
                newMap += c;
            else
                newMap += c;
            newMap += ' ';
        }

        map.Text = newMap;
    }
}