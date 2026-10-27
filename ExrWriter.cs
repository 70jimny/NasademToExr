using TinyEXR;

namespace NasademToExr;

internal static class ExrWriter
{
    public static void Write(string path, Heightmap map)
    {
        // 各チャネルは同一のFloat32標高列を共有し、正規化・色変換は行わない。
        var bytes = new byte[checked(map.Values.Length * sizeof(float))];
        Buffer.BlockCopy(map.Values, 0, bytes, 0, bytes.Length);
        var header = new ExrHeader
        {
            Compression = CompressionType.ZIP,
            DataWindow = new ExrBox2i(0, 0, map.Width - 1, map.Height - 1),
            DisplayWindow = new ExrBox2i(0, 0, map.Width - 1, map.Height - 1)
        };
        var channels = new List<ExrImageChannel>();
        foreach (string name in new[] { "B", "G", "R" })
        {
            var channel = new ExrChannel(name, ExrPixelType.Float);
            header.Channels.Add(channel);
            channels.Add(new(channel, ExrPixelType.Float, bytes));
        }
        var image = new ExrImage(map.Width, map.Height, channels);
        // 同じボリュームの一時ファイルから確定し、失敗時は既存EXRを保持する。
        string temporary = path + $".{Guid.NewGuid():N}.tmp";
        var result = Exr.SaveEXRImageToFile(image, header, temporary);
        if (result != ResultCode.Success)
            throw new IOException($"EXR保存失敗 ({result}): {temporary}");
        if (File.Exists(path)) File.Replace(temporary, path, null);
        else File.Move(temporary, path);
    }
}
