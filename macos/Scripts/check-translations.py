#!/usr/bin/env python3
"""Mac arayuzundeki her metnin tr/de/es cevirisi var mi?

Mac, Windows'la ayni Strings_<kod>.json dosyalarini kullanir (anahtar Ingilizce
metin; DockHubCore/Localizer.swift). Bu betik macos/Sources altindaki ceviri
anahtarlarini toplar:
  - L.t("...") cagrilari,
  - WidgetRegistry'deki widget, kategori ve varyant adlari (displayName),
  - WidgetCatalog aciklamalari (Info(summary: "...")),
  - kisayol eylemlerinin ad ve aciklamalari (HotkeyAction),
  - WeatherService'teki hava durumu metinleri (description).
Her anahtarin uc dilde de bos olmayan bir cevirisi olmali.

Kullanim: macos/Scripts/check-translations.py   Cikis kodu: 0 tamam, 1 eksik var.
"""
import json, pathlib, re, sys

MACOS = pathlib.Path(__file__).resolve().parents[1]
RESOURCES = MACOS.parent / "src" / "CustomDock" / "Resources"
LANGS = ["tr", "de", "es"]

LITERAL = r'"((?:[^"\\]|\\.)*)"'
CALLS = [
    re.compile(r'\bL\.t\(' + LITERAL),
    re.compile(r'WidgetDefinition\(id: "[^"]*", name: ' + LITERAL + r', category: ' + LITERAL),
    re.compile(r'WidgetVariant\("[^"]*", ' + LITERAL + r'\)'),
    re.compile(r'Info\(summary: ' + LITERAL),
    re.compile(r'HotkeyAction\(id: \w+, name: ' + LITERAL + r', description: ' + LITERAL),
]


def unescape(s: str) -> str:
    return re.sub(r'\\(.)', lambda m: {"n": "\n", "t": "\t"}.get(m.group(1), m.group(1)), s)


def load(code: str) -> dict:
    text = (RESOURCES / f"Strings_{code}.json").read_text(encoding="utf-8")
    text = re.sub(r'^\s*//.*$', '', text, flags=re.M)
    return json.loads(text)


def keys() -> dict:
    found = {}
    for path in sorted((MACOS / "Sources").rglob("*.swift")):
        if path.name == "SelfTests.swift":        # komut satiri oz-testleri, arayuz degil
            continue
        text = path.read_text(encoding="utf-8")
        for pattern in CALLS:
            for m in pattern.finditer(text):
                for group in m.groups():
                    if group and "\\(" not in group:
                        found.setdefault(unescape(group), f"{path.relative_to(MACOS)}:{text[:m.start()].count(chr(10)) + 1}")
        if path.name == "WeatherService.swift":
            block = text[text.index("public var description: String"):]
            block = block[:block.index("\n    }\n")]
            for m in re.finditer(r'case [^:]+:\s+' + LITERAL, block):
                if m.group(1) != "-":
                    found.setdefault(m.group(1), f"{path.relative_to(MACOS)}")
    return found


def main() -> int:
    found = keys()
    tables = {code: load(code) for code in LANGS}
    missing = {k: [c for c in LANGS if not tables[c].get(k)] for k in found}
    missing = {k: v for k, v in missing.items() if v}
    print(f"Mac ceviri anahtari: {len(found)}")
    for key, codes in sorted(missing.items()):
        print(f"  EKSIK {','.join(codes)}: {key!r}  ({found[key]})")
    print("SONUC: " + ("TAMAM" if not missing else f"{len(missing)} EKSIK"))
    return 1 if missing else 0


if __name__ == "__main__":
    sys.exit(main())
