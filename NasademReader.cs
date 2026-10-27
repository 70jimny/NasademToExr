using System.Globalization;
using System.Text.RegularExpressions;

namespace NasademToExr;

internal readonly record struct GeoBounds(double South, double West, double North, double East)
{
    public static GeoBounds Parse(string text)
    {
        var parts = text.Split(',');
        var values = new double[4];
        for (int i = 0; i < 4; i++)
        {
            if (parts.Length != 4 || !double.TryParse(parts[i], NumberStyles.Float,
                    CultureInfo.InvariantCulture, out values[i]) || !double.IsFinite(values[i]))
                throw new InputException($"--bounds は有限の数値4個を 南,西,北,東 の順に指定してください: {text}");
        }
        var bounds = new GeoBounds(values[0], values[1], values[2], values[3]);
        bounds.Validate();
        return bounds;
    }

    public void Validate()
    {
        if (South < -80 || North > 84 || South >= North || West < -180 || East > 180 || West >= East)
            throw new InputException($"UTM対象外または大小関係が不正な範囲: {this}");
        if (South < 0 && North > 0)
            throw new InputException($"赤道をまたぐ範囲は単一UTM座標系で扱えません: {this}");
        // 東端がちょうどゾーン境界の場合は、その西側ゾーンの境界として扱う。
        int zone = Math.Clamp((int)Math.Floor(((West + East) / 2 + 180) / 6) + 1, 1, 60);
        if (West < -180 + (zone - 1) * 6 || East > -180 + zone * 6)
            throw new InputException($"UTMゾーンをまたぐ範囲です: {this}");
    }

    public override string ToString() => FormattableString.Invariant($"{South},{West},{North},{East}");
}

internal sealed record NasademTile(string Name, string Path, int South, int West);
internal sealed record NasademInput(NasademTile[] Tiles, GeoBounds Bounds, string OutputPath);

internal static partial class NasademReader
{
    internal const int Side = 3601;
    private const long Samples = (long)Side * Side;

    [GeneratedRegex("^([ns])([0-9]{2})([ew])([0-9]{3})$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TilePattern();

    public static NasademInput Read(string folder, GeoBounds? requested)
    {
        if (!Directory.Exists(folder))
            throw new InputException($"入力フォルダがありません: {folder}");
        folder = Path.GetFullPath(folder);
        // 関連拡張子だけを対象にし、補助ファイルだけの孤立セットも検査する。
        var groups = Directory.EnumerateFiles(folder)
            .Where(p => new[] { ".hgt", ".num", ".swb" }.Contains(Path.GetExtension(p), StringComparer.OrdinalIgnoreCase))
            .GroupBy(Path.GetFileNameWithoutExtension, StringComparer.OrdinalIgnoreCase);
        var tiles = new List<NasademTile>();
        var positions = new HashSet<(int, int)>();
        foreach (var group in groups)
        {
            string name = group.Key!.ToLowerInvariant();
            var match = TilePattern().Match(name);
            if (!match.Success)
                throw new InputException($"タイル名が不正です: {group.First()}");
            int south = int.Parse(match.Groups[2].Value) * (match.Groups[1].Value == "s" ? -1 : 1);
            int west = int.Parse(match.Groups[4].Value) * (match.Groups[3].Value == "w" ? -1 : 1);
            if (south < -90 || south >= 90 || west < -180 || west >= 180 || !positions.Add((south, west)))
                throw new InputException($"範囲外または重複したタイルです: {name}");
            string? hgt = null;
            foreach (string extension in new[] { ".hgt", ".num", ".swb" })
            {
                var files = group.Where(p => Path.GetExtension(p).Equals(extension, StringComparison.OrdinalIgnoreCase)).ToArray();
                if (files.Length != 1)
                    throw new InputException($"ファイルの欠落または重複: {Path.Combine(folder, name + extension)}");
                long expected = Samples * (extension == ".hgt" ? 2 : 1);
                if (new FileInfo(files[0]).Length != expected)
                    throw new InputException($"ファイルサイズ異常 (期待値 {expected} byte): {files[0]}");
                if (extension == ".hgt") hgt = files[0];
            }
            tiles.Add(new(name, hgt!, south, west));
        }
        if (tiles.Count == 0)
            throw new InputException($"HGT/NUM/SWBセットがありません: {folder}");
        var bounds = requested ?? new GeoBounds(tiles.Min(t => t.South), tiles.Min(t => t.West),
            tiles.Max(t => t.South) + 1, tiles.Max(t => t.West) + 1);
        bounds.Validate();
        // 整数度セルを列挙して、対象矩形の内側をタイル集合が完全に覆うか確認する。
        for (int lat = (int)Math.Floor(bounds.South); lat < Math.Ceiling(bounds.North); lat++)
            for (int lon = (int)Math.Floor(bounds.West); lon < Math.Ceiling(bounds.East); lon++)
                if (!positions.Contains((lat, lon)))
                    throw new InputException($"対象範囲に欠落タイルがあります: 南西座標 {lat},{lon} / {folder}");
        return new(tiles.OrderBy(t => t.Name, StringComparer.Ordinal).ToArray(), bounds,
            Path.Combine(folder, tiles.Count == 1 ? tiles[0].Name + ".exr" : "nasadem.exr"));
    }
}
