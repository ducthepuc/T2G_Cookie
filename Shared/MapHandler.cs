using T2G_Cookie.Shared.Values;

namespace T2G_Cookie.Shared;

public class MapHandler
{
    // row -> column

    readonly char[,] map;

    // The grid exactly as it was loaded; ResetMap puts everything back from it.
    readonly char[,] _original;

    MapElement[,] parsableMap;

    // Cells whose gold was mined this session (row, col), for display only: the
    // parsed element is a Wall either way, exactly as MachineSpirit leaves it.
    readonly HashSet<(int Row, int Col)> _mined = [];

    // The state before the current run: Start checkpoints, Stop restores.
    char[,]? _runMap;
    MapElement[,]? _runParsable;
    HashSet<(int Row, int Col)>? _runMined;
    
    public enum MapElement : long
    {
        Empty = 0,
        Wall = 1,
        Gold = 2
    }

    public MapHandler(char[,] map)
    {
        this.map = map;
        _original = (char[,])map.Clone();
        ParseMap();
    }

    public int Rows => map.GetLength(0);
    public int Cols => map.GetLength(1);

    /// <summary>Gold cells the map shipped with, fixed at parse time so mining
    /// cannot shrink the denominator.</summary>
    public int TotalGold { get; private set; }

    /// <summary>Gold mined so far - the count behind MachineSpirit.GoldMined: the
    /// manual MINE button and the VM's Mine block both end in <see cref="Mine"/>.</summary>
    public int GoldMined => _mined.Count;

    /// <summary>The parsed element at a grid cell, for viewers such as the GUI map panel.</summary>
    public MapElement ElementAt(int row, int col) => parsableMap[row, col];

    /// <summary>
    /// Puts the robot marker at the cell the VM's MachineSpirit stands on, facing the
    /// way it faces - how the run view follows the spirit after every instruction. The
    /// cell the robot leaves goes back to its terrain, so the matrix stays consistent
    /// for saving and for the next IdentifyRobot.
    /// </summary>
    public bool PlaceRobot(Vector2Int position, Direction facing)
    {
        if (position.X < 0 || position.Y < 0 || position.X >= Cols || position.Y >= Rows)
            return false;

        Vector2Int? current = IdentifyRobot(out _);
        if (current is { } old && (old.X != position.X || old.Y != position.Y))
            map[old.Y, old.X] = CharOf(parsableMap[old.Y, old.X]);

        map[position.Y, position.X] = CharOf(facing);
        return true;
    }

    /// <summary>Turns the robot marker in place; false when the map has no robot.</summary>
    public bool TurnRobotLeft()
    {
        Vector2Int? pos = IdentifyRobot(out Direction? facing);
        if (pos == null || facing == null) return false;
        map[pos.Value.Y, pos.Value.X] = CharOf(facing.Value.TurnLeft());
        return true;
    }

    /// <summary>See <see cref="TurnRobotLeft"/>; the mirror turn.</summary>
    public bool TurnRobotRight()
    {
        Vector2Int? pos = IdentifyRobot(out Direction? facing);
        if (pos == null || facing == null) return false;
        map[pos.Value.Y, pos.Value.X] = CharOf(facing.Value.TurnRight());
        return true;
    }

    /// <summary>
    /// Steps the robot one cell forward: false at the map edge or into a wall, so a
    /// manual driver can never push it through terrain. Mined cells read as walls
    /// but are walkable - green means the gold is gone, not that the way is blocked
    /// (the VM keeps MachineSpirit's stricter rule). The cell it leaves goes back to
    /// its terrain, which keeps the matrix consistent for saving and for the
    /// simulator picking the position up later.
    /// </summary>
    public bool StepRobot()
    {
        Vector2Int? pos = IdentifyRobot(out Direction? facing);
        if (pos == null || facing == null) return false;

        int col = pos.Value.X + facing.Value.ModX;
        int row = pos.Value.Y + facing.Value.ModY;
        if (row < 0 || row >= Rows || col < 0 || col >= Cols) return false;
        if (parsableMap[row, col] == MapElement.Wall && !IsMined(row, col)) return false;

        map[pos.Value.Y, pos.Value.X] = CharOf(parsableMap[pos.Value.Y, pos.Value.X]);
        map[row, col] = CharOf(facing.Value);
        return true;
    }

    static char CharOf(MapElement element) => element switch
    {
        MapElement.Wall => '#',
        MapElement.Gold => '@',
        _ => '.',
    };

    static char CharOf(Direction facing) => facing.ModX switch
    {
        1 => '>',
        -1 => '<',
        _ when facing.ModY < 0 => '^',
        _ => 'v',
    };

    void ParseMap()
    {
        parsableMap = new MapElement[map.GetLength(0),map.GetLength(1)];
        int totalGold = 0;
        for (int row = 0; row < map.GetLength(0); row++)
        {
            for (int col = 0; col < map.GetLength(1); col++)
            {
                MapElement? element = CharToMapElement(map[row, col]);
                if (element.HasValue)
                {
                    parsableMap[row, col] = element.Value;
                    if (element.Value == MapElement.Gold) totalGold++;
                }
                else
                    throw new Exception($"Unknown character {map[row, col]}");
            }
        }
        TotalGold = totalGold;
    }

    public Vector2Int? IdentifyRobot(out Direction? direction)
    {
        direction = null;
        for (int row = 0; row < map.GetLength(0); row++)
        {
            for (int col = 0; col < map.GetLength(1); col++)
            {
                var dir = CharToDirection(map[row, col]);
                if (dir.HasValue)
                {
                    direction = dir;
                    return new Vector2Int(col, row);  
                } 
            }
        }

        return null;
    }

    static MapElement? CharToMapElement(char element)
    {
        if (CharToDirection(element).HasValue) return MapElement.Empty;
        if (element == '.') return MapElement.Empty;
        if (element == '#') return MapElement.Wall;
        if (element == '@') return MapElement.Gold;

        return null;
    }

    static Direction? CharToDirection(char state)
    {
        if (state == '^')
            return Direction.Up;

        // Both cases: the file format's map identifiers use lowercase 'v'.
        if (state == 'V' || state == 'v')
            return Direction.Down;

        if (state == '<')
            return Direction.Left;

        if (state == '>')
            return Direction.Right;

        return null;        
    }

    public MapElement GetForward(Direction direction, Vector2Int position)
    {
        if (position.Y + direction.ModY >= parsableMap.GetLength(0) || position.X + direction.ModX >= parsableMap.GetLength(1))
        {
            return MapElement.Wall; 
        }else if (position.Y + direction.ModY < 0 || position.X + direction.ModX < 0)
        {
            return MapElement.Wall;
        }

        return parsableMap[position.Y + direction.ModY, position.X + direction.ModX];
    }

    public bool Mine(Vector2Int pos)
    {
        ref var mapThere = ref parsableMap[pos.Y, pos.X];
        if (mapThere != MapElement.Gold) return false;

        mapThere = MapElement.Wall;
        _mined.Add((pos.Y, pos.X));

        return true;
    }

    /// <summary>True for a cell mined this session: the map itself cannot tell it
    /// apart from an original wall, but viewers want to show it differently.</summary>
    public bool IsMined(int row, int col) => _mined.Contains((row, col));

    /// <summary>Puts every mined cell back to gold - and back into the matrix, except
    /// under the robot, whose marker has to stay for <see cref="IdentifyRobot"/>.</summary>
    public void ResetMined()
    {
        Vector2Int? robot = IdentifyRobot(out _);
        foreach ((int row, int col) in _mined)
        {
            parsableMap[row, col] = MapElement.Gold;
            if (robot is { } standing && standing.X == col && standing.Y == row) continue;
            map[row, col] = '@';
        }
        _mined.Clear();
    }

    /// <summary>
    /// Puts the whole map back to the state it was loaded in - unlike
    /// <see cref="ResetMined"/>, which keeps the robot where it stands: the terrain
    /// matrix becomes the original one again (so the robot marker sits on its
    /// original cell facing its original way), the mined marks are gone, and the
    /// parsed elements are rebuilt from those original cells.
    /// </summary>
    public void ResetMap()
    {
        for (int row = 0; row < Rows; row++)
            for (int col = 0; col < Cols; col++)
                map[row, col] = _original[row, col];

        _mined.Clear();
        ParseMap(); // parsableMap + TotalGold rebuilt from the restored matrix
    }

    /// <summary>Remembers the map exactly as it is now - terrain matrix, parsed
    /// elements, mined marks - so a later <see cref="RestoreCheckpoint"/> can put the
    /// state back (the run session's Start does this before the program touches it).</summary>
    public void Checkpoint()
    {
        _runMap = (char[,])map.Clone();
        _runParsable = (MapElement[,])parsableMap.Clone();
        _runMined = new HashSet<(int Row, int Col)>(_mined);
    }

    /// <summary>Puts the map back to the last <see cref="Checkpoint"/>: the robot's
    /// cell and orientation, the mined gold, and the parsed elements. No checkpoint
    /// yet means nothing to restore.</summary>
    public void RestoreCheckpoint()
    {
        if (_runMap == null || _runParsable == null || _runMined == null) return;

        Array.Copy(_runMap, map, _runMap.Length);
        Array.Copy(_runParsable, parsableMap, _runParsable.Length);
        _mined.Clear();
        foreach ((int row, int col) in _runMined)
            _mined.Add((row, col));
    }

    /// <summary>Mines the cell the robot stands on - what MachineSpirit's Mine block
    /// does: gold turns into a wall, anything else (or no robot) is refused.</summary>
    public bool MineRobot()
    {
        Vector2Int? pos = IdentifyRobot(out _);
        return pos != null && Mine(pos.Value);
    }

    public MapElement[,] GetElements() => parsableMap;
}