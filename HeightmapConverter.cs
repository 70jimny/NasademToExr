namespace NasademToExr;

internal sealed record Heightmap(int Width, int Height, double West, double North, float[] Values);
internal readonly record struct GridRectangle(int Left, int Top, int Width, int Height)
{
    public long Area => (long)Width * Height;
}

internal static class HeightmapConverter
{
    public static Heightmap Convert(TerrainProjector projector)
    {
        var envelope = projector.ProjectedEnvelope();
        int width = checked((int)Math.Round((envelope.East - envelope.West) / 30) + 1);
        int height = checked((int)Math.Round((envelope.North - envelope.South) / 30) + 1);
        var x = new double[width];
        var y = new double[width];
        var z = new double[width];
        var heights = new int[width];
        var stack = new int[width + 1];
        var best = new GridRectangle();
        for (int row = 0; row < height; row++)
        {
            projector.InverseRow(envelope.West, envelope.North - row * 30, x, y, z);
            // 有効列の連続高さを積み上げ、行ごとに最大のヒストグラム矩形を求める。
            for (int col = 0; col < width; col++)
                heights[col] = projector.CanInterpolate(x[col], y[col]) ? heights[col] + 1 : 0;
            FindRectangle(heights, row, stack, ref best);
        }
        if (best.Area == 0)
            throw new InputException("対象範囲内に30m格子の有効な矩形がありません。");
        double west = envelope.West + best.Left * 30;
        double north = envelope.North - best.Top * 30;
        projector.ValidateNoData(west, north, best.Width, best.Height);
        var values = new float[checked(best.Width * best.Height)];
        x = new double[best.Width]; y = new double[best.Width]; z = new double[best.Width];
        // 確定した矩形を再投影して補間する。欠損があれば縮小せずその場で中断する。
        for (int row = 0; row < best.Height; row++)
        {
            projector.InverseRow(west, north - row * 30, x, y, z);
            for (int col = 0; col < best.Width; col++)
                values[row * best.Width + col] = projector.Sample(x[col], y[col]);
        }
        return new(best.Width, best.Height, west, north, values);
    }

    internal static void FindRectangle(int[] heights, int row, int[] stack, ref GridRectangle best)
    {
        int size = 0;
        for (int col = 0; col <= heights.Length; col++)
        {
            int current = col == heights.Length ? 0 : heights[col];
            // 高さが減る位置で矩形の右端が確定するため、各列を一度だけ取り出す。
            while (size > 0 && heights[stack[size - 1]] > current)
            {
                int h = heights[stack[--size]];
                int left = size == 0 ? 0 : stack[size - 1] + 1;
                var candidate = new GridRectangle(left, row - h + 1, col - left, h);
                // 同面積では北（小さいTop）、次に西（小さいLeft）を優先する。
                if (candidate.Area > best.Area || candidate.Area == best.Area &&
                    (candidate.Top < best.Top || candidate.Top == best.Top && candidate.Left < best.Left))
                    best = candidate;
            }
            stack[size++] = col;
        }
    }
}
