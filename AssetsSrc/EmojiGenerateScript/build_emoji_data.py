"""
EmojiGenerateScript/emoji_master.xlsx(build_emoji_master_csv.pyで作ったたたき台を
人手で整備したもの)を正として、CutADash/Assets/Emoji配下のkind別JSON(絵文字一覧)と
スプライトシートPNG(scale-100/200/300、10列)を作り直す。

emoji_master.xlsxの行構成:
  1行 = 1バリエーション(基底自身を含む)。"unicode"列(異体字なしの基底コードポイント)が
  同じ絵文字どうしをまとめるキーになり、基底行は"variant_unicode"が空、肌の色
  バリエーションの行は"variant_unicode"にそのバリエーションのコードポイントが入る。

メインのスプライトシート(<group>.scale-*.png、10列)には基底のみを含める。
バリエーションを持つアイテムは、実際のバリエーション絵文字(グリフ文字列)を
"variants"配列としてJSONに持たせ、さらにツールチップでの画像表示用に、
バリエーションだけを集めた別のスプライトシート(<group>.variants.scale-*.png、
5列=肌の色5種、1行=バリエーションを持つアイテム1件)も生成する。

使い方: py -3 AssetsSrc/EmojiGenerateScript/build_emoji_data.py
出力:
  - CutADash/Assets/Emoji/<group>.json (絵文字一覧。"has_variants"/"variants"付き)
  - CutADash/Assets/Emoji/<group>.scale-{100,200,300}.png (メインスプライトシート、10列)
  - CutADash/Assets/Emoji/<group>.variants.scale-{100,200,300}.png
    (バリエーションのスプライトシート、5列。無ければ生成しない)
"""

import json
import os
import re

from openpyxl import load_workbook
from PIL import Image

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
ASSETS_SRC_DIR = os.path.dirname(SCRIPT_DIR)
REPO_ROOT = os.path.dirname(ASSETS_SRC_DIR)
MASTER_PATH = os.path.join(SCRIPT_DIR, "emoji_master.xlsx")
OUTPUT_DIR = os.path.join(REPO_ROOT, "CutADash", "Assets", "Emoji")

DEST_COLUMNS = 10
VARIANT_COLUMNS = 5
SCALES = {100: 32, 200: 64, 300: 96}


def to_file_name_stem(kind):
    # EmojiRepository.ToFileNameStem(C#側)と揃える
    return kind.replace(" ", "_").replace("&", "and")


class Entry:
    def __init__(self, glyph, text, png_path):
        self.glyph = glyph
        self.text = text
        self.png_path = png_path
        self.variants = []
        self.variant_png_paths = []

    @property
    def has_variants(self):
        return len(self.variants) > 0


def collect_entries():
    """group名 -> Entryのリスト、を返す。emoji_master.xlsxの行順をそのまま踏襲する
    (並び順の調整はxlsx側で行う前提のため、ここでは並べ替えない)。"""
    wb = load_workbook(MASTER_PATH, read_only=True, data_only=True)
    ws = wb.active

    # 列の並びは1行目のヘッダー名から解決する。group_no/sort_no(A/B列、並び替え用の
    # 補助列)とunicode(加工)/variant_unicode(加工)(G/H列、ソート用に加工した値)は
    # ここでは使わない(実際に使うのはgroup/text/unicode/variant_unicode/emoji/fluent_path)
    required_columns = ["group", "text", "unicode", "variant_unicode", "emoji", "fluent_path"]

    all_rows = ws.iter_rows(values_only=True)
    header = list(next(all_rows))
    col = {name: header.index(name) for name in required_columns}
    rows = list(all_rows)

    groups = {}
    current_entry = None

    for row in rows:
        if row is None or all(v is None for v in row):
            continue

        group = row[col["group"]]
        text = row[col["text"]]
        variant_unicode = row[col["variant_unicode"]]
        emoji = row[col["emoji"]]
        fluent_path = row[col["fluent_path"]]

        if not group or not emoji:
            continue

        if not variant_unicode:
            # 基底行: 新しいEntryを作ってbucketへ積む(fluent_pathはfluentui-emojiサブモジュール
            # 基準の相対パス。サブモジュールはAssetsSrc配下にあるため、そちらを基準に解決する)
            png_path = os.path.join(ASSETS_SRC_DIR, fluent_path) if fluent_path else None
            current_entry = Entry(emoji, text, png_path)
            groups.setdefault(group, []).append(current_entry)
        else:
            # バリエーション行: メインシートには含めないが、グリフ文字列と画像パス(後で
            # バリエーション専用シートを作るのに使う)を直前の基底Entryへ積む
            if current_entry is not None:
                variant_png_path = os.path.join(ASSETS_SRC_DIR, fluent_path) if fluent_path else None
                current_entry.variants.append(emoji)
                current_entry.variant_png_paths.append(variant_png_path)

    wb.close()
    return groups


def write_json(group, entries):
    stem = to_file_name_stem(group)
    items = [
        {
            "emoji": e.glyph,
            "text": e.text,
            "has_variants": e.has_variants,
            "variants": e.variants,
        }
        for e in entries
    ]
    payload = {"kind": group, "items": items}
    out_path = os.path.join(OUTPUT_DIR, f"{stem}.json")
    with open(out_path, "w", encoding="utf-8") as f:
        json.dump(payload, f, ensure_ascii=False, indent=2)
    return stem


def write_sprite_sheets(stem, entries):
    count = len(entries)
    rows = max(1, -(-count // DEST_COLUMNS))

    # 元画像(256x256)は各scaleぶんまとめてキャッシュしながら使い回す
    source_cache = {}

    for scale, cell in SCALES.items():
        sheet = Image.new("RGBA", (DEST_COLUMNS * cell, rows * cell), (0, 0, 0, 0))

        for i, entry in enumerate(entries):
            if entry.png_path is None or not os.path.isfile(entry.png_path):
                continue

            src = source_cache.get(entry.png_path)
            if src is None:
                src = Image.open(entry.png_path).convert("RGBA")
                source_cache[entry.png_path] = src

            tile = src.resize((cell, cell), Image.LANCZOS)
            col = i % DEST_COLUMNS
            row = i // DEST_COLUMNS
            sheet.paste(tile, (col * cell, row * cell), tile)

        out_path = os.path.join(OUTPUT_DIR, f"{stem}.scale-{scale}.png")
        sheet.save(out_path)


def write_variant_sprite_sheets(stem, entries):
    """バリエーションを持つアイテムだけを対象に、1アイテム=1行・5列(肌の色5種)の
    スプライトシートを作る。ツールチップでのバリエーション表示に使う想定。
    バリエーションを持つアイテムが1件も無ければ何も作らない。"""
    variant_entries = [e for e in entries if e.has_variants]
    if not variant_entries:
        return

    source_cache = {}

    for scale, cell in SCALES.items():
        sheet = Image.new("RGBA", (VARIANT_COLUMNS * cell, len(variant_entries) * cell), (0, 0, 0, 0))

        for row, entry in enumerate(variant_entries):
            for col, png_path in enumerate(entry.variant_png_paths):
                if png_path is None or not os.path.isfile(png_path):
                    continue

                src = source_cache.get(png_path)
                if src is None:
                    src = Image.open(png_path).convert("RGBA")
                    source_cache[png_path] = src

                tile = src.resize((cell, cell), Image.LANCZOS)
                sheet.paste(tile, (col * cell, row * cell), tile)

        out_path = os.path.join(OUTPUT_DIR, f"{stem}.variants.scale-{scale}.png")
        sheet.save(out_path)


def main():
    if not os.path.isfile(MASTER_PATH):
        raise SystemExit(
            f"{MASTER_PATH} が見つかりません。"
            "先にAssetsSrc/EmojiGenerateScript/build_emoji_master_csv.pyでたたき台を作るか、"
            "手で用意してください"
        )

    os.makedirs(OUTPUT_DIR, exist_ok=True)

    # 既存のkind別JSON/PNGを一旦消す(groupの数・内容が変わりうるため)
    for fname in os.listdir(OUTPUT_DIR):
        if fname.endswith(".json") or re.search(r"\.scale-\d+\.png$", fname):
            os.remove(os.path.join(OUTPUT_DIR, fname))

    groups = collect_entries()

    for group, entries in groups.items():
        stem = write_json(group, entries)
        write_sprite_sheets(stem, entries)
        write_variant_sprite_sheets(stem, entries)
        variant_count = sum(1 for e in entries if e.has_variants)
        missing = sum(1 for e in entries if e.png_path is None or not os.path.isfile(e.png_path))
        print(
            f"{group}: {len(entries)} 件(うちバリエーション持ち{variant_count}件、"
            f"画像欠落{missing}件) -> {stem}.json / {stem}.scale-*.png"
        )


if __name__ == "__main__":
    main()
