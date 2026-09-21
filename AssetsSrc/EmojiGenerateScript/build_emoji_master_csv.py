"""
fluentui-emoji サブモジュール(リポジトリ直下)のmetadata.jsonから、絵文字マスターの
たたき台となるxlsxを作る。以降のデータ整備(並び順の調整、欠けている絵文字の追記、
グループ分けの見直し等)は、このxlsxを人手で編集する前提。

1行 = 1バリエーション(基底自身を含む)。同じ絵文字の行は"unicode"列(異体字なしの
基底コードポイント)が共通のキーになり、基底行は"variant_unicode"が空、肌の色
バリエーションの行は"variant_unicode"にそのバリエーションのコードポイントが入る。

使い方: py -3 AssetsSrc/EmojiGenerateScript/build_emoji_master_csv.py
出力: EmojiGenerateScript/emoji_master.xlsx
"""

import json
import os

from openpyxl import Workbook
from openpyxl.utils import get_column_letter

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
REPO_ROOT = os.path.dirname(SCRIPT_DIR)
ASSETS_DIR = os.path.join(REPO_ROOT, "fluentui-emoji", "assets")
OUTPUT_PATH = os.path.join(SCRIPT_DIR, "emoji_master.xlsx")

TONE_ORDER = ["Light", "Medium-Light", "Medium", "Medium-Dark", "Dark"]
TONE_FOLDER_BY_MODIFIER = {
    0x1F3FB: "Light",
    0x1F3FC: "Medium-Light",
    0x1F3FD: "Medium",
    0x1F3FE: "Medium-Dark",
    0x1F3FF: "Dark",
}

HEADER = ["group", "text", "unicode", "variant_unicode", "emoji", "fluent_path"]


def find_png(style_dir):
    if not os.path.isdir(style_dir):
        return None
    pngs = [p for p in os.listdir(style_dir) if p.endswith(".png")]
    return os.path.join(style_dir, pngs[0]) if pngs else None


def relpath(png_path):
    if png_path is None:
        return ""
    return os.path.relpath(png_path, REPO_ROOT).replace("\\", "/")


def codepoints_to_hex(s):
    return " ".join(f"{ord(c):x}" for c in s)


def collect_rows():
    rows = []

    for name in sorted(os.listdir(ASSETS_DIR)):
        folder = os.path.join(ASSETS_DIR, name)
        meta_path = os.path.join(folder, "metadata.json")
        if not os.path.isfile(meta_path):
            continue

        with open(meta_path, encoding="utf-8") as f:
            meta = json.load(f)

        glyph = meta.get("glyph")
        group = meta.get("group")
        text = meta.get("tts") or meta.get("cldr") or name
        if not glyph or not group:
            continue

        base_unicode = codepoints_to_hex(glyph)

        direct_png = find_png(os.path.join(folder, "3D"))
        if direct_png:
            # 肌色バリエーションを持たない絵文字は基底の1行だけ
            rows.append([group, text, base_unicode, "", glyph, relpath(direct_png)])
            continue

        default_png = find_png(os.path.join(folder, "Default", "3D"))
        if not default_png:
            continue

        rows.append([group, text, base_unicode, "", glyph, relpath(default_png)])

        for tone_seq in meta.get("unicodeSkintones", [])[1:]:
            codepoints = [int(cp, 16) for cp in tone_seq.split(" ")]
            tone_glyph = "".join(chr(cp) for cp in codepoints)
            modifier = next((cp for cp in codepoints if cp in TONE_FOLDER_BY_MODIFIER), None)
            if modifier is None:
                continue
            tone_folder_name = TONE_FOLDER_BY_MODIFIER[modifier]
            tone_png = find_png(os.path.join(folder, tone_folder_name, "3D"))
            rows.append([
                group, text, base_unicode, codepoints_to_hex(tone_glyph),
                tone_glyph, relpath(tone_png),
            ])

    return rows


def main():
    if not os.path.isdir(ASSETS_DIR):
        raise SystemExit(
            f"{ASSETS_DIR} が見つかりません。"
            "fluentui-emojiサブモジュールが初期化されているか確認してください"
            "(git submodule update --init --recursive)"
        )

    rows = collect_rows()

    wb = Workbook()
    ws = wb.active
    ws.title = "emoji_master"
    ws.append(HEADER)
    ws.freeze_panes = "A2"
    for row in rows:
        ws.append(row)

    # 列幅をざっくり内容に合わせておく(絵文字列は固定でやや広め、フォントの都合で
    # 実際の見た目幅はOS依存になる)
    widths = {"group": 16, "text": 30, "unicode": 16, "variant_unicode": 16, "emoji": 8, "fluent_path": 70}
    for i, col in enumerate(HEADER, start=1):
        ws.column_dimensions[get_column_letter(i)].width = widths.get(col, 16)

    wb.save(OUTPUT_PATH)
    print(f"{len(rows)} 行を出力しました -> {OUTPUT_PATH}")


if __name__ == "__main__":
    main()
