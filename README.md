# NasademToExr

## 概要

NasademToExrは、NASA Earthdataの標高データ **NASADEM_HGT.001** を、Godotの **Terrain3D v1.1.0-rc1** で利用するためのOpenEXR（`.exr`）形式に変換する、Windows向けのC#コマンドラインツールです。Godotとは独立して動作します。

フォルダ内の同名タイルの `.hgt`・`.num`・`.swb` を入力とし、必要に応じて隣接タイルを結合して、同じフォルダに `.exr` ファイルを生成します。標高はメートル単位の値を保持したRGB・32bit浮動小数点形式です。地理座標をUTMに投影し、水平解像度を30m間隔に揃えます。投影後の矩形EXRでは、指定範囲の端部が一部除外される場合があります。

## 使い方

### 1. 準備

Windows 11に **.NET 10 SDK（10.0.402）** を用意します。NASADEM_HGT.001から取得したZIPを展開し、次のように3ファイルを同じフォルダに配置します。

```text
D:\TerrainData\Iki\
├── n33e129.hgt
├── n33e129.num
└── n33e129.swb
```

### 2. ビルド

PowerShellでリポジトリのフォルダに移動してビルドします。

```powershell
Set-Location "D:\source\02_Windows\NasademToExr"
dotnet build -c Release
```

### 3. EXRへ変換

**3ファイルが入ったフォルダのパス**を、引数に1つ指定します。

```powershell
& "D:\source\02_Windows\NasademToExr\bin\Release\net10.0\NasademToExr.exe" "D:\TerrainData\Iki"
```

対象範囲を緯度経度で切り出す場合は、`--bounds` に **南端,西端,北端,東端** の順で指定します（10進度数）。省略した場合は入力タイルの範囲を対象にします。

```powershell
& "D:\source\02_Windows\NasademToExr\bin\Release\net10.0\NasademToExr.exe" "D:\TerrainData\Iki" --bounds "33.1,129.2,33.8,129.9"
```

出力先：

```text
D:\TerrainData\Iki\n33e129.exr
```

### 4. Godot Terrain3Dへ取り込む

Godotで `addons/terrain_3d/tools/importer.tscn` を開き、`Importer` ノードのHeight Mapに生成した `n33e129.exr` を指定します。

- `import_scale`：`1`
- `height_offset`：`0`
- Terrain3Dの `Mesh / Vertex Spacing`：`30`（m）

`Run Import` を実行し、`Data Directory` に保存します。
