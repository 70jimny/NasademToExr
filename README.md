# NasademToExr

## 概要

NasademToExrは、NASA Earthdataの標高データ **NASADEM_HGT.001** を、Godotの **Terrain3D v1.1.0-rc1** で利用するためのOpenEXR（`.exr`）形式に変換する、Windows向けのC#コマンドラインツールです。Godotとは独立して動作します。

フォルダ内の同名タイルの `.hgt`・`.num`・`.swb` を入力とし、必要に応じて隣接タイルを結合して、同じフォルダに `.exr` ファイルを生成します。標高はメートル単位の値を保持したRGB・32bit浮動小数点形式です。地理座標をUTMに投影し、水平解像度を30m間隔に揃えます。投影後の矩形EXRでは、指定範囲の端部が一部除外される場合があります。

## 使い方

### 1. 準備

Windows 11 x64で使用します。完成版はリポジトリ直下の `release\NasademToExr.exe` です。`release` 内のDLL・データフォルダも必要なので、EXEだけを移動せずフォルダ全体を保持してください。.NETランタイムは同梱されています。

NASADEM_HGT.001から取得したZIPを展開し、次のように3ファイルを同じフォルダに配置します。

```text
D:\TerrainData\Iki\
├── n33e129.hgt
├── n33e129.num
└── n33e129.swb
```

### 2. ソースから発行する場合

ビルド時は **.NET SDK 10.0.403** が必要です。`global.json`で固定しているため、別バージョンへの自動切替は行いません。PowerShellで次を実行すると、ランタイムとGDAL/PROJを含むWindows x64版を `release` に発行します。

```powershell
Set-Location "D:\source\02_Windows\NasademToExr"
dotnet publish -c Release -r win-x64 --self-contained true -o .\release
```

### 3. EXRへ変換

**3ファイルが入ったフォルダのパス**を、引数に1つ指定します。

```powershell
& "D:\source\02_Windows\NasademToExr\release\NasademToExr.exe" "D:\TerrainData\Iki"
```

対象範囲を緯度経度で切り出す場合は、`--bounds` に **南端,西端,北端,東端** の順で指定します（10進度数）。省略した場合は入力タイルの範囲を対象にします。

```powershell
& "D:\source\02_Windows\NasademToExr\release\NasademToExr.exe" "D:\TerrainData\Iki" --bounds "33.1,129.2,33.8,129.9"
```

出力先：

```text
D:\TerrainData\Iki\n33e129.exr
```

複数タイルの場合は `nasadem.exr` になります。既存EXRは変換に成功した場合だけ置き換えます。欠落タイル、補間に使うNoData、UTMゾーン・赤道をまたぐ範囲はエラーになります。終了コードは成功 `0`、引数・入力・範囲の不正 `1`、読込・投影・保存の失敗 `2` です。

指定の壱岐島データで実行する場合：

```powershell
& "D:\source\02_Windows\NasademToExr\release\NasademToExr.exe" "C:\Data\Download\NASADEM_壱岐島"
```

`n33e129` 全域の確認結果は `3079×3681` 画素、EPSG:32652、水平間隔30mです。標高を正規化しないため、一般的な画像ビューアでは白く表示される場合があります。

### 4. Godot Terrain3Dへ取り込む

Godotで `addons/terrain_3d/tools/importer.tscn` を開き、`Importer` ノードの `Import File / Height File Name` に生成した `n33e129.exr` を指定します。

- `height_scale`：`1`
- `height_offset`：`0`
- Terrain3Dの `Mesh / Vertex Spacing`：`30`（m）

`Run Import` を実行し、`Destination Directory` を指定して `Save to Disk` で保存します。

画像の右が東（Godotの+X）、上が北（-Z）です。Terrain3Dは領域サイズ単位で画像端をパディングするため、EXR外側の余白は元データの範囲に含めません。

入力データ・EXR・`release` のバイナリはGit管理対象外です。実装仕様と主要な検証結果は [詳細設計書](docs/specification/v0.1.0/NasademToExr_詳細設計書.md) を参照してください。
