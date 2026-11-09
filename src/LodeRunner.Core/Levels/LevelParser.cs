namespace LodeRunner.Core.Levels;

public sealed class LevelParseException : Exception
{
    public LevelParseException(string message, int line = 0)
        : base(line > 0 ? $"Line {line}: {message}" : message)
    {
    }
}

/// <summary>
/// Text level format: optional ';' metadata lines (e.g. "; name: Test 1"),
/// then exactly <see cref="Rows"/> map lines of up to <see cref="Cols"/> characters.
/// Short lines are padded with spaces.
/// </summary>
public static class LevelParser
{
    public const int Cols = 28;
    public const int Rows = 16;

    public static LevelData Parse(string text, string fallbackName = "Unnamed")
    {
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

        string name = fallbackName;
        var map = new List<(string Text, int LineNo)>();

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            if (line.StartsWith(';'))
            {
                string meta = line[1..].Trim();
                if (meta.StartsWith("name:", StringComparison.OrdinalIgnoreCase))
                    name = meta[5..].Trim();
                continue;
            }
            map.Add((line, i + 1));
        }

        // Trailing blank lines (e.g. final newline) are not map rows.
        while (map.Count > Rows && map[^1].Text.Length == 0)
            map.RemoveAt(map.Count - 1);

        if (map.Count != Rows)
            throw new LevelParseException($"Expected {Rows} map rows, found {map.Count}.");

        var tiles = new TileType[Cols, Rows];
        var gold = new List<Cell>();
        var enemies = new List<Cell>();
        Cell? player = null;

        for (int r = 0; r < Rows; r++)
        {
            var (line, lineNo) = map[r];
            if (line.Length > Cols)
                throw new LevelParseException($"Row is {line.Length} chars, maximum is {Cols}.", lineNo);

            for (int c = 0; c < Cols; c++)
            {
                char ch = c < line.Length ? line[c] : ' ';
                var cell = new Cell(c, r);
                switch (ch)
                {
                    case ' ': break;
                    case '#': tiles[c, r] = TileType.Brick; break;
                    case '@': tiles[c, r] = TileType.Concrete; break;
                    case 'H': tiles[c, r] = TileType.Ladder; break;
                    case '-': tiles[c, r] = TileType.Rope; break;
                    case 'S': tiles[c, r] = TileType.HiddenLadder; break;
                    case 'X': tiles[c, r] = TileType.TrapBrick; break;
                    case '$': gold.Add(cell); break;
                    case '0': enemies.Add(cell); break;
                    case '&':
                        if (player is not null)
                            throw new LevelParseException("More than one player start '&'.", lineNo);
                        player = cell;
                        break;
                    default:
                        throw new LevelParseException($"Unknown character '{ch}' at column {c + 1}.", lineNo);
                }
            }
        }

        if (player is null)
            throw new LevelParseException("No player start '&'.");
        if (gold.Count == 0)
            throw new LevelParseException("Level has no gold '$'.");

        return new LevelData(name, tiles, player.Value, enemies, gold);
    }

    public static LevelData Load(string path) =>
        Parse(File.ReadAllText(path), Path.GetFileNameWithoutExtension(path));
}
