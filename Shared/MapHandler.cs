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