using T2G_Cookie.Shared.Values;

namespace T2G_Cookie.Shared;

public class MapHandler
{
    // row -> column

    readonly char[,] map;

    MapElement[,] parsableMap;
    
    public enum MapElement : long
    {
        Empty = 0,
        Wall = 1,
        Gold = 2
    }

    public MapHandler(char[,] map)
    {
        this.map = map;
        ParseMap();
    }

    public int Rows => map.GetLength(0);
    public int Cols => map.GetLength(1);

    /// <summary>The parsed element at a grid cell, for viewers such as the GUI map panel.</summary>
    public MapElement ElementAt(int row, int col) => parsableMap[row, col];

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
    /// manual driver can never push it through terrain. The cell it leaves goes back
    /// to its terrain - a gold it stood on reappears there - which keeps the matrix
    /// consistent for saving and for the simulator picking the position up later.
    /// </summary>
    public bool StepRobot()
    {
        Vector2Int? pos = IdentifyRobot(out Direction? facing);
        if (pos == null || facing == null) return false;

        int col = pos.Value.X + facing.Value.ModX;
        int row = pos.Value.Y + facing.Value.ModY;
        if (row < 0 || row >= Rows || col < 0 || col >= Cols) return false;
        if (parsableMap[row, col] == MapElement.Wall) return false;

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
        for (int row = 0; row < map.GetLength(0); row++)
        {
            for (int col = 0; col < map.GetLength(1); col++)
            {
                MapElement? element = CharToMapElement(map[row, col]);
                if (element.HasValue)
                    parsableMap[row, col] = element.Value;
                else
                    throw new Exception($"Unknown character {map[row, col]}");
            }
        }
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

        return true;
    }

    public MapElement[,] GetElements() => parsableMap;
}