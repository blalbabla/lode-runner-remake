using LodeRunner.Core.Levels;

namespace LodeRunner.Core.Tests;

public class LevelParserTests
{
    private static string Map(params string[] overrides)
    {
        // 16 empty rows; overrides are (row index encoded as "idx:content").
        var rows = Enumerable.Repeat(new string(' ', LevelParser.Cols), LevelParser.Rows).ToArray();
        foreach (var o in overrides)
        {
            int sep = o.IndexOf(':');
            rows[int.Parse(o[..sep])] = o[(sep + 1)..];
        }
        return string.Join("\n", rows);
    }

    [Fact]
    public void ParsesAllTileTypesAndMarks()
    {
        var level = LevelParser.Parse(Map("0:#@H-S", "1:$&0"));

        Assert.Equal(LevelParser.Cols, level.Cols);
        Assert.Equal(LevelParser.Rows, level.Rows);
        Assert.Equal(TileType.Brick, level[0, 0]);
        Assert.Equal(TileType.Concrete, level[1, 0]);
        Assert.Equal(TileType.Ladder, level[2, 0]);
        Assert.Equal(TileType.Rope, level[3, 0]);
        Assert.Equal(TileType.HiddenLadder, level[4, 0]);
        Assert.Equal(new Cell(0, 1), level.Gold.Single());
        Assert.Equal(new Cell(1, 1), level.PlayerStart);
        Assert.Equal(new Cell(2, 1), level.EnemyStarts.Single());
        Assert.Equal(TileType.Empty, level[0, 1]);
    }

    [Fact]
    public void ReadsNameFromMetadata()
    {
        var level = LevelParser.Parse("; name: My Level\n" + Map("0:$&"));
        Assert.Equal("My Level", level.Name);
    }

    [Fact]
    public void UsesFallbackNameWhenNoMetadata()
    {
        var level = LevelParser.Parse(Map("0:$&"), "fallback");
        Assert.Equal("fallback", level.Name);
    }

    [Fact]
    public void ShortAndWindowsLineEndingsAreAccepted()
    {
        var level = LevelParser.Parse(Map("0:$&").Replace("\n", "\r\n") + "\r\n");
        Assert.Equal(LevelParser.Rows, level.Rows);
    }

    [Fact]
    public void ShortRowsArePaddedWithEmpty()
    {
        var level = LevelParser.Parse(Map("0:$&", "5:#"));
        Assert.Equal(TileType.Brick, level[0, 5]);
        Assert.Equal(TileType.Empty, level[27, 5]);
    }

    [Fact]
    public void WrongRowCountThrows()
    {
        var ex = Assert.Throws<LevelParseException>(() => LevelParser.Parse("$&\n#"));
        Assert.Contains("16", ex.Message);
    }

    [Fact]
    public void TooLongRowThrowsWithLineNumber()
    {
        var ex = Assert.Throws<LevelParseException>(() =>
            LevelParser.Parse(Map("0:$&", "3:" + new string('#', 29))));
        Assert.StartsWith("Line 4:", ex.Message);
    }

    [Fact]
    public void UnknownCharacterThrows()
    {
        var ex = Assert.Throws<LevelParseException>(() => LevelParser.Parse(Map("0:$&", "2:?")));
        Assert.Contains("'?'", ex.Message);
    }

    [Fact]
    public void MissingPlayerThrows() =>
        Assert.Throws<LevelParseException>(() => LevelParser.Parse(Map("0:$")));

    [Fact]
    public void TwoPlayersThrow() =>
        Assert.Throws<LevelParseException>(() => LevelParser.Parse(Map("0:$&&")));

    [Fact]
    public void NoGoldThrows() =>
        Assert.Throws<LevelParseException>(() => LevelParser.Parse(Map("0:&")));
}
