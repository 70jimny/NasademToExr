using MaxRev.Gdal.Core;
using OSGeo.GDAL;
using OSGeo.OSR;

namespace NasademToExr;

internal sealed class TerrainProjector : IDisposable
{
    private readonly SpatialReference geographic = new("");
    private readonly SpatialReference utm = new("");
    private CoordinateTransformation? forward;
    private CoordinateTransformation? inverse;
    private readonly List<Dataset> sources = [];
    private Dataset? mosaic;
    private readonly string vrtPath = $"/vsimem/nasadem-{Guid.NewGuid():N}.vrt";
    private short[] elevations = [];
    private int sourceWidth;
    private int sourceHeight;
    private double sourceWest;
    private double sourceNorth;
    internal const double Step = 1.0 / 3600;
    public GeoBounds Bounds { get; }
    public int Epsg { get; }

    public TerrainProjector(NasademInput input)
    {
        Bounds = input.Bounds;
        int zone = Math.Clamp((int)Math.Floor(((Bounds.West + Bounds.East) / 2 + 180) / 6) + 1, 1, 60);
        Epsg = (Bounds.North <= 0 ? 32700 : 32600) + zone;
        try
        {
            GdalBase.ConfigureAll();
            Gdal.UseExceptions();
            geographic.ImportFromEPSG(4326);
            utm.ImportFromEPSG(Epsg);
            // 地理座標はX=経度/Y=緯度とし、鉛直座標変換を含めない。
            geographic.SetAxisMappingStrategy(AxisMappingStrategy.OAMS_TRADITIONAL_GIS_ORDER);
            utm.SetAxisMappingStrategy(AxisMappingStrategy.OAMS_TRADITIONAL_GIS_ORDER);
            forward = new CoordinateTransformation(geographic, utm);
            inverse = new CoordinateTransformation(utm, geographic);
            foreach (var tile in input.Tiles)
            {
                var dataset = Gdal.OpenEx(tile.Path, 2, ["SRTMHGT"], null, null)
                    ?? throw new IOException($"SRTMHGT読込失敗: {tile.Path}");
                sources.Add(dataset);
                ValidateTile(dataset, tile);
            }
            // 同じ1秒角格子に結合し、NoDataを別タイルから自動補完しない。
            using var options = new GDALBuildVRTOptions(["-resolution", "highest", "-srcnodata", "None", "-vrtnodata", "-32768"]);
            mosaic = Gdal.BuildVRT(vrtPath, sources.ToArray(), options, null, null)
                ?? throw new IOException("VRTモザイクの生成に失敗しました。");
            LoadWindow();
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    private static void ValidateTile(Dataset dataset, NasademTile tile)
    {
        var transform = new double[6];
        dataset.GetGeoTransform(transform);
        using var band = dataset.GetRasterBand(1);
        // 半画素分の外周と北から南への走査方向を実データの地理参照で確認する。
        if (dataset.RasterXSize != NasademReader.Side || dataset.RasterYSize != NasademReader.Side ||
            dataset.RasterCount != 1 || band.DataType != DataType.GDT_Int16 ||
            Math.Abs(transform[0] + Step / 2 - tile.West) > 1e-9 ||
            Math.Abs(transform[3] - Step / 2 - tile.South - 1) > 1e-9 ||
            Math.Abs(transform[1] - Step) > 1e-12 || Math.Abs(transform[5] + Step) > 1e-12 ||
            transform[2] != 0 || transform[4] != 0)
            throw new InputException($"HGTの形式・地理参照が不正です: {tile.Path}");
    }

    private void LoadWindow()
    {
        var transform = new double[6];
        mosaic!.GetGeoTransform(transform);
        double west = transform[0] + Step / 2;
        double north = transform[3] - Step / 2;
        // boundsの外側の標高を補間に混ぜないよう、内側にあるサンプルだけを読む。
        int left = (int)Math.Ceiling((Bounds.West - west) / Step - 1e-7);
        int right = (int)Math.Floor((Bounds.East - west) / Step + 1e-7);
        int top = (int)Math.Ceiling((north - Bounds.North) / Step - 1e-7);
        int bottom = (int)Math.Floor((north - Bounds.South) / Step + 1e-7);
        sourceWidth = right - left + 1;
        sourceHeight = bottom - top + 1;
        if (sourceWidth < 2 || sourceHeight < 2)
            throw new InputException("対象範囲が小さすぎて双線形補間できません。");
        sourceWest = west + left * Step;
        sourceNorth = north - top * Step;
        elevations = new short[checked(sourceWidth * sourceHeight)];
        using var band = mosaic.GetRasterBand(1);
        if (band.ReadRaster(left, top, sourceWidth, sourceHeight, elevations, sourceWidth, sourceHeight, 0, 0) != CPLErr.CE_None)
            throw new IOException("HGTモザイクの標高を読み取れません。");
    }

    public (double West, double South, double East, double North) ProjectedEnvelope()
    {
        // 境界を1秒角以下に細分化し、曲線の極値を囲む余裕を1格子分取る。
        int count = Math.Max(2, (int)Math.Ceiling(Math.Max(Bounds.East - Bounds.West, Bounds.North - Bounds.South) / Step));
        var x = new double[(count + 1) * 4];
        var y = new double[x.Length];
        for (int i = 0; i <= count; i++)
        {
            double lon = Bounds.West + (Bounds.East - Bounds.West) * i / count;
            double lat = Bounds.South + (Bounds.North - Bounds.South) * i / count;
            int j = i * 4;
            x[j] = x[j + 1] = lon;
            y[j] = Bounds.South; y[j + 1] = Bounds.North;
            x[j + 2] = Bounds.West; x[j + 3] = Bounds.East;
            y[j + 2] = y[j + 3] = lat;
        }
        forward!.TransformPoints(x.Length, x, y, new double[x.Length]);
        if (x.Any(v => !double.IsFinite(v)) || y.Any(v => !double.IsFinite(v)))
            throw new IOException("UTM境界の投影に失敗しました。");
        return (Math.Floor(x.Min() / 30) * 30 - 30, Math.Floor(y.Min() / 30) * 30 - 30,
            Math.Ceiling(x.Max() / 30) * 30 + 30, Math.Ceiling(y.Max() / 30) * 30 + 30);
    }

    public void InverseRow(double west, double north, double[] x, double[] y, double[] z)
    {
        for (int i = 0; i < x.Length; i++)
        {
            x[i] = west + i * 30;
            y[i] = north;
            z[i] = 0;
        }
        inverse!.TransformPoints(x.Length, x, y, z);
        // 座標変換の失敗を幾何的な範囲外と混同して出力矩形を縮めない。
        if (x.Any(v => !double.IsFinite(v)) || y.Any(v => !double.IsFinite(v)))
            throw new IOException("UTM格子の逆投影に失敗しました。");
    }

    public bool CanInterpolate(double lon, double lat)
    {
        // 標高の値は参照しない。NoDataによって最大矩形を縮めることを防ぐ。
        double x = (lon - sourceWest) / Step;
        double y = (sourceNorth - lat) / Step;
        return double.IsFinite(x) && double.IsFinite(y) && x >= 0 && y >= 0 &&
            x <= sourceWidth - 1 && y <= sourceHeight - 1;
    }

    public float Sample(double lon, double lat)
    {
        if (!CanInterpolate(lon, lat))
            throw new IOException($"補間可能な範囲の外です: {lon:R},{lat:R}");
        double x = (lon - sourceWest) / Step, y = (sourceNorth - lat) / Step;
        int ix = Math.Min((int)Math.Floor(x), sourceWidth - 2);
        int iy = Math.Min((int)Math.Floor(y), sourceHeight - 2);
        double fx = x - ix, fy = y - iy;
        double result = 0;
        for (int dy = 0; dy <= 1; dy++)
            for (int dx = 0; dx <= 1; dx++)
            {
                double weight = (dx == 0 ? 1 - fx : fx) * (dy == 0 ? 1 - fy : fy);
                if (weight == 0) continue;
                short value = elevations[(iy + dy) * sourceWidth + ix + dx];
                if (value == short.MinValue)
                    throw new IOException($"出力領域の補間にNoData(-32768)が含まれます: 経度={lon:R}, 緯度={lat:R}");
                result += value * weight;
            }
        return (float)result;
    }

    public void ValidateNoData(double west, double north, int width, int height)
    {
        double east = west + (width - 1) * 30;
        double south = north - (height - 1) * 30;
        var point = new double[3];
        // 再標本化で参照されない入力点も、出力矩形の内側なら欠損を見逃さない。
        for (int i = 0; i < elevations.Length; i++)
        {
            if (elevations[i] != short.MinValue) continue;
            double lon = sourceWest + i % sourceWidth * Step;
            double lat = sourceNorth - i / sourceWidth * Step;
            forward!.TransformPoint(point, lon, lat, 0);
            if (point[0] >= west && point[0] <= east && point[1] >= south && point[1] <= north)
                throw new IOException($"出力領域内の入力HGTにNoData(-32768)があります: 経度={lon:R}, 緯度={lat:R}");
        }
    }

    public void Dispose()
    {
        inverse?.Dispose();
        forward?.Dispose();
        utm.Dispose();
        geographic.Dispose();
        mosaic?.Dispose();
        foreach (var source in sources) source.Dispose();
        // VRTはメモリだけに置くため、入力フォルダにGIS中間ファイルは残らない。
        if (mosaic is not null) Gdal.Unlink(vrtPath);
    }
}
