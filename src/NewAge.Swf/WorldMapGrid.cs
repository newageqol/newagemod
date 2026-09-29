using NewAge.Swf.Display;

namespace NewAge.Swf;

public sealed class WorldMapGrid
{
    public const int CellSize = 15;

    private readonly bool[,] _passable;

    private WorldMapGrid(int width, int height)
    {
        Width = width;
        Height = height;
        _passable = new bool[width, height];
    }

    public int Width { get; }
    public int Height { get; }
    public int PassableCount { get; private set; }

    public bool IsPassable(int x, int y) =>
        x >= 0 && y >= 0 && x < Width && y < Height && _passable[x, y];

    public double PixelWidth => Width * CellSize;
    public double PixelHeight => Height * CellSize;

    public static (int X, int Y) CellAt(double pixelX, double pixelY) =>
        ((int)Math.Floor(pixelX / CellSize), (int)Math.Floor(pixelY / CellSize));

    public static WorldMapGrid Load(SwfMovie movie)
    {
        if (movie is null) return null;

        var pushes = Avm1Scanner.Pushes(movie.File, movie.File.Tags);
        if (pushes.Count == 0) return null;

        int width = 0, height = 0;
        for (int i = 0; i + 1 < pushes.Count; i++)
        {
            if (pushes[i].Is("mapWidth") && pushes[i + 1].IsNumber) width = (int)pushes[i + 1].Number;
            else if (pushes[i].Is("mapHeigth") && pushes[i + 1].IsNumber) height = (int)pushes[i + 1].Number;
            if (width > 0 && height > 0) break;
        }

        if (width <= 0 || height <= 0) return null;

        var grid = new WorldMapGrid(width, height);
        for (int i = 0; i + 3 < pushes.Count; i++)
        {
            if (!pushes[i].Is("s")) continue;
            if (!pushes[i + 1].IsNumber || !pushes[i + 2].IsNumber || !pushes[i + 3].IsNumber) continue;
            if (Math.Abs(pushes[i + 3].Number - 1) > 0.0001) continue;

            int x = (int)pushes[i + 1].Number;
            int y = (int)pushes[i + 2].Number;
            if (x < 0 || y < 0 || x >= width || y >= height) continue;
            if (grid._passable[x, y]) continue;

            grid._passable[x, y] = true;
            grid.PassableCount++;
        }

        return grid.PassableCount > 0 ? grid : null;
    }

    public override string ToString() =>
        $"сетка {Width}×{Height}, проходимых клеток {PassableCount}, клетка {CellSize} px";
}
