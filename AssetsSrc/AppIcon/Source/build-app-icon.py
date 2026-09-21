"""このフォルダ(AssetsSrc/AppIcon/Source、リポジトリ直下)のAppIcon.png(Affinity Designerで
手作業エクスポートしたもの)から、実際にビルドで使われるCutADash/Assets/AppIcon.png
(1024x1024の1枚絵)とAppIcon.ico(複数解像度をまとめたアイコンリソース。
020_CutADash.csprojのApplicationIconが参照)を作り直す。

以前はAppIcon.svgをヘッドレスEdgeでラスタライズしていたが、デザイン自体を
Affinity Designerで手作業編集するようになったため、その書き出し結果(PNG)を
そのまま信頼して使うだけにした(build-tray-icon.pyと同じ考え方)。

    使い方: python3 AssetsSrc/AppIcon/Source/build-app-icon.py
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
SRC_PNG_PATH = SRC_DIR / "AppIcon.png"
PNG_OUT = OUT_DIR / "AppIcon.png"
ICO_OUT = OUT_DIR / "AppIcon.ico"

# ICOに詰める解像度。16/20/24/32/40/48/64/128/256はWindowsの標準DPI(100%~300%程度)で
# 実際に使われるサイズの一式(元のAppIcon.icoに合わせている)
ICO_SIZES = [16, 20, 24, 32, 40, 48, 64, 128, 256]

# ビルドで使うAppIcon.png(1枚絵)の解像度
RENDER_SIZE = 1024


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


def main() -> None:
    if not SRC_PNG_PATH.exists():
        raise SystemExit(
            f"{SRC_PNG_PATH} が見つかりませんでした。"
            "Affinity DesignerからAppIcon.pngを書き出してから実行してください。"
        )

    OUT_DIR.mkdir(parents=True, exist_ok=True)

    for path in (PNG_OUT, ICO_OUT):
        if path.exists():
            # protect-assets.ps1で読み取り専用にされている場合、書き込み前に解除しておく
            path.chmod(0o666)

    src = Image.open(SRC_PNG_PATH).convert("RGBA")

    # ビルドで使うAppIcon.png(1枚絵)は固定解像度に正規化しておく
    master = resize_premultiplied(src, (RENDER_SIZE, RENDER_SIZE))
    master.save(PNG_OUT)
    print(f"  AppIcon.png ({RENDER_SIZE}x{RENDER_SIZE})")

    # PillowのICO書き出しは、sizesに渡したサイズのフレームが見つからない場合、
    # 自前でthumbnail(size, LANCZOS, reducing_gap=None)を呼んで作る。それだと
    # 元画像からの一段階の大幅縮小になるうえstraight alphaのままのため、輪郭の
    # 角にリンギング(元の色にない斑点)が出る不具合を実際に踏んだ。そのため
    # 各サイズをpremultiply込みで自前で縮小してから、サイズが完全一致する
    # フレームとして渡す(内部のthumbnail呼び出しを回避する)
    frames = [resize_premultiplied(src, (size, size)) for size in ICO_SIZES]

    # PillowのICO保存は、先頭に渡した画像より大きいサイズのフレームを黙って取りこぼす
    # (im.encoderinfo["sizes"]をim.size基準でフィルタするため)。最大サイズを先頭にする
    frames.sort(key=lambda f: f.size, reverse=True)
    frames[0].save(
        ICO_OUT, sizes=[f.size for f in frames], append_images=frames[1:]
    )

    sizes_label = ", ".join(f"{s}x{s}" for s in ICO_SIZES)
    print(f"  AppIcon.ico ({sizes_label})")

    print()
    print(f"完了: {OUT_DIR}")


if __name__ == "__main__":
    main()
