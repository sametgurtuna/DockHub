#!/usr/bin/env python3
"""Windows AppConfig.cs semasini macOS'un yazdigi config.json ile karsilastirir.

C# tarafindaki public property adlari JsonNamingPolicy.CamelCase ile camelCase
anahtara donusur. Bu betik o listeyi kodu ayristirarak cikarir ve Swift'in
gercekten yazdigi dosyanin anahtarlariyla karsilastirir.

Kullanim: macos/Scripts/compare-config-schema.py [config.json yolu]
Cikis kodu: 0 uyumlu, 1 fark var.
"""
import json, os, re, sys, pathlib

REPO = pathlib.Path(__file__).resolve().parents[2]
CS = REPO / "src" / "CustomDock" / "Core" / "AppConfig.cs"
DEFAULT_CFG = pathlib.Path(os.path.expanduser(
    "~/Library/Application Support/DockHub/config.json"))

PROP = re.compile(r"^\s*public\s+[\w<>,\s\?\[\]]+?\s+(\w+)\s*\{\s*get")


def camel(name: str) -> str:
    return name[0].lower() + name[1:]


def parse_cs(path: pathlib.Path):
    """AppConfig sinifindaki public property'leri dondurur."""
    lines = path.read_text(encoding="utf-8").splitlines()
    start = end = None
    for i, l in enumerate(lines):
        if "class AppConfig" in l:
            start = i
        elif start is not None and re.match(r"^\s*public\s+sealed\s+class\s+\w+", l) \
                and "AppConfig" not in l:
            end = i
            break
    body = lines[start:end if end else len(lines)]

    always, omit_when_null, ignored = [], [], []
    attrs = []
    for l in body:
        s = l.strip()
        if s.startswith("["):
            attrs.append(s)
            continue
        m = PROP.match(l)
        if m:
            name = camel(m.group(1))
            joined = " ".join(attrs)
            if "[JsonIgnore]" in joined:
                ignored.append(name)
            elif "WhenWritingNull" in joined:
                omit_when_null.append(name)
            else:
                always.append(name)
        if s and not s.startswith("["):
            attrs = []
    return always, omit_when_null, ignored


def main() -> int:
    cfg_path = pathlib.Path(sys.argv[1]) if len(sys.argv) > 1 else DEFAULT_CFG
    if not CS.exists():
        print(f"HATA: Windows kaynagi bulunamadi: {CS}"); return 1
    if not cfg_path.exists():
        print(f"HATA: config.json bulunamadi: {cfg_path}\n"
              f"Once uygulamayi bir kez calistirin."); return 1

    always, omit_null, ignored = parse_cs(CS)
    win_all = set(always) | set(omit_null)
    mac_keys = set(json.loads(cfg_path.read_text(encoding="utf-8")).keys())

    print(f"Windows kaynagi : {CS.relative_to(REPO)}")
    print(f"macOS ciktisi   : {cfg_path}")
    print()
    print(f"C# public property        : {len(win_all)}")
    print(f"  her zaman yazilan       : {len(always)}")
    print(f"  nil ise yazilmayan      : {len(omit_null)} -> {sorted(omit_null)}")
    print(f"  [JsonIgnore] (serialize edilmez): {len(ignored)} -> {sorted(ignored)}")
    print(f"macOS config.json anahtari: {len(mac_keys)}")
    print()

    eksik = sorted(set(always) - mac_keys)          # olmasi gerekirken yok
    fazla = sorted(mac_keys - win_all)              # Windows'ta olmayan
    atlanan = sorted(set(omit_null) - mac_keys)     # beklenen sekilde yok

    ok = True
    if eksik:
        ok = False
        print("HATA - her zaman yazilmasi gereken ama macOS ciktisinda OLMAYAN:")
        for k in eksik: print(f"   - {k}")
    if fazla:
        ok = False
        print("HATA - Windows semasinda olmayan ama macOS'un yazdigi:")
        for k in fazla: print(f"   + {k}")
    if atlanan:
        print("BEKLENEN - nil oldugu icin yazilmayan v1 uyum alanlari:")
        for k in atlanan: print(f"   . {k}")
    print()
    print("SONUC: " + ("SEMA UYUMLU" if ok else "SEMA FARKI VAR"))
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
