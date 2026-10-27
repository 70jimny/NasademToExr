using System.Globalization;

namespace NasademToExr;

internal static class Program
{
    private static int Main(string[] args)
    {
        // 小数点と診断表示をOSの地域設定から独立させる。
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        string context = args.Length > 0 ? args[0] : "(フォルダ未指定)";
        try
        {
            if (args.Length != 1 && (args.Length != 3 || args[1] != "--bounds"))
                throw new InputException("使い方: NasademToExr.exe <フォルダ> [--bounds 南,西,北,東]");
            var requested = args.Length == 3 ? GeoBounds.Parse(args[2]) : (GeoBounds?)null;
            var input = NasademReader.Read(args[0], requested);
            Console.WriteLine($"入力タイル: {string.Join(", ", input.Tiles.Select(t => t.Name))}");
            Console.WriteLine($"--bounds: {(requested.HasValue ? "適用" : "未指定")} / 地理範囲: {input.Bounds}");
            using var projector = new TerrainProjector(input);
            Console.WriteLine($"UTM: EPSG:{projector.Epsg}");
            var map = HeightmapConverter.Convert(projector);
            context = input.OutputPath;
            ExrWriter.Write(context, map);
            Console.WriteLine($"EXR画素数: {map.Width} x {map.Height} / 水平間隔: X=30m, Y=30m");
            Console.WriteLine($"標高: 最小={map.Values.Min():R}m, 最大={map.Values.Max():R}m (EGM96)");
            Console.WriteLine($"UTM画素中心範囲: 西={map.West:R}, 南={map.North - (map.Height - 1) * 30:R}, 東={map.West + (map.Width - 1) * 30:R}, 北={map.North:R} m");
            Console.WriteLine("地理的な指定境界と出力矩形は異なります。内接矩形の外側の端部は除外されます。");
            Console.WriteLine($"EXR保存先: {context}");
            return 0;
        }
        catch (InputException ex)
        {
            Console.Error.WriteLine($"入力エラー [{context}]: {ex.Message}");
            return 1;
        }
        catch (Exception ex)
        {
            // ネイティブ初期化・読込・投影・保存の失敗は原因を残して終了する。
            Console.Error.WriteLine($"変換エラー [{context}]: {ex.Message}");
            return 2;
        }
    }
}

internal sealed class InputException(string message) : Exception(message);
