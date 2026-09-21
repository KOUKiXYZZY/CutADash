"""システムトレイ(通知領域)専用のアイコンを、Affinity Designerで手作業エクスポートした
PNG(TaskTrayIconLight.png / TaskTrayIconDark.png)からICOへパッキングするだけのスクリプト。

背景: 通知領域アイコンは、アプリ本体のアイコン(AppIcon.ico、角丸タイル+グラデーション
背景)をそのまま使うと小さいサイズでは潰れて見づらいため、背景タイルを持たないグリフのみ
のアイコンをライト/ダーク別に用意し、実行時にTrayIcon.SetIcon()でテーマに応じて
切り替える(App側の実装はTaskTray.cs UpdateTrayIconForTheme参照)。

以前はAppIcon.svgからヘッドレスEdgeで自動ラスタライズしていたが、デザイン自体を
Affinity Designerで手作業編集するようになったため、その書き出し結果(PNG)を
そのまま信頼してICOに詰めるだけにした。ラスタライズ/色抽出のロジックを自前で
持たないぶん、デザイン側の見た目がそのまま反映される。

    使い方: python3 AssetsSrc/AppIcon/Source/build-tray-icon.py
"""

import sys
from pathlib import Path

from PIL import Image, ImageChops, ImageMath

# Windowsのコンソールはデフォルトでcp932等になっていることがあり、日本語の
# print()がUnicodeEncodeErrorになったり文字化けしたりするため、明示的にUTF-8へ
if sys.stdout.encoding is None or sys.stdout.encoding.lower() != "utf-8":
    sys.stdout.reconfigure(encoding="utf-8")

SRC_DIR = Path(__file__).resolve().parent
REPO_ROOT = SRC_DIR.parents[2]
OUT_DIR = REPO_ROOT / "CutADash" / "Assets"

# 通知領域アイコンの実サイズ(100%/125%/150%/200%/250%/300%/400%相当)
ICO_SIZES = [16, 20, 24, 32, 40, 48, 64]

# 「Light」「Dark」はシステムのテーマ名(=通知領域の背景の明暗)。それぞれに対応する
# PNGをAffinity Designerの書き出し(TaskTrayIcon<テーマ名>.png)からそのまま使う
THEMES = ["Light", "Dark"]

# 正方形キャンバスへ収める際の余白比率(上下左右それぞれこの割合を空ける)
PAD_RATIO = 0.08


def resize_premultiplied(im: Image.Image, size: tuple[int, int]) -> Image.Image:
    """透明部分のゴミ色が縁ににじまないよう、アルファを事前乗算してから縮小する。

    PillowはRGBAを「straight alpha」のまま縮小するため、完全透明ピクセルの下に
    残っているゴミ色(Affinity書き出しでは任意の値になりうる)が縁ににじみ出て、
    輪郭の角に緑/ピンクの斑点が出る不具合を実際に踏んだ。アルファを事前乗算
    (premultiply)してから縮小し、縮小後に戻す(unpremultiply)ことで、透明部分の
    ゴミ色が混ざらないようにする。
    """
    im = im.convert("RGBA")
    r, g, b, a = im.split()
    premultiplied = Image.merge(
        "RGBA",
        (
            ImageChops.multiply(r, a),
            ImageChops.multiply(g, a),
            ImageChops.multiply(b, a),
            a,
        ),
    )
    resized = premultiplied.resize(size, Image.LANCZOS, reducing_gap=3.0)
    r2, g2, b2, a2 = resized.split()

    def unpremultiply(channel: Image.Image) -> Image.Image:
        # a2が0の画素は完全透明で見えないため、0除算を避けるためだけに+1しておけば結果は無関係
        return ImageMath.unsafe_eval(
            'convert(c * 255 / (a + (a == 0)), "L")', c=channel, a=a2
        )

    return Image.merge(
        "RGBA", (unpremultiply(r2), unpremultiply(g2), unpremultiply(b2), a2)
    )


def build_theme_icon(theme_name: str) -> None:
    png_path = SRC_DIR / f"TaskTrayIcon{theme_name}.png"
    if not png_path.exists():
        raise SystemExit(
            f"{png_path} が見つかりませんでした。"
            "Affinity DesignerからPNGを書き出してから実行してください。"
        )

    ico_out = OUT_DIR / f"AppIconTray{theme_name}.ico"
    if ico_out.exists():
        # protect-assets.ps1で読み取り専用にされている場合、書き込み前に解除しておく
        ico_out.chmod(0o666)

    src = Image.open(png_path).convert("RGBA")
    sw, sh = src.size

    # 書き出しPNGは正方形とは限らないため、長辺基準で余白少なめの
    # 正方形キャンバス(透過背景、元画像とほぼ同じ解像度)にまず収める
    canvas_size = max(sw, sh)
    inner = int(canvas_size * (1 - PAD_RATIO * 2))
    scale = inner / max(sw, sh)
    placed_size = (max(1, round(sw * scale)), max(1, round(sh * scale)))
    placed = resize_premultiplied(src, placed_size)

    master = Image.new("RGBA", (canvas_size, canvas_size), (0, 0, 0, 0))
    offset = (
        (canvas_size - placed_size[0]) // 2,
        (canvas_size - placed_size[1]) // 2,
    )
    master.paste(placed, offset, placed)

    # PillowのICO書き出しは、sizesに渡したサイズのフレームが見つからない場合、
    # 自前でthumbnail(size, LANCZOS, reducing_gap=None)を呼んで作る。それだと
    # 大きな画像(元は900px超)から16px前後への一段階の大幅縮小になるうえ
    # straight alphaのままのため、輪郭の角にリンギング(元の色にない斑点)が
    # 出る不具合を実際に踏んだ。そのため各サイズをpremultiply込みで自前で
    # 縮小してから、サイズが完全一致するフレームとして渡す
    # (内部のthumbnail呼び出しを回避する)
    frames = [resize_premultiplied(master, (size, size)) for size in ICO_SIZES]

    # PillowのICO保存は、先頭に渡した画像より大きいサイズのフレームを黙って取りこぼす
    # (im.encoderinfo["sizes"]をim.size基準でフィルタするため)。最大サイズを先頭にする
    frames.sort(key=lambda f: f.size, reverse=True)
    frames[0].save(
        ico_out, sizes=[f.size for f in frames], append_images=frames[1:]
    )

    sizes_label = ", ".join(str(s) for s in ICO_SIZES)
    print(f"  AppIconTray{theme_name}.ico ({sizes_label})")


def main() -> None:
    OUT_DIR.mkdir(parents=True, exist_ok=True)
    for theme_name in THEMES:
        build_theme_icon(theme_name)
    print()
    print(f"完了: {OUT_DIR}")


if __name__ == "__main__":
    main()
