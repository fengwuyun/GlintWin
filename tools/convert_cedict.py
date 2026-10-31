import gzip
import json
import os
import re
import sys
import urllib.request

URL = "https://www.mdbg.net/chinese/export/cedict/cedict_1_0_ts_utf-8_mdbg.txt.gz"
HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.normpath(os.path.join(HERE, "..", "Data", "cedict.json"))
RAW = os.path.join(HERE, "cedict_raw.txt.gz")

LINE_RE = re.compile(r"^(\S+)\s+(\S+)\s+\[([^\]]+)\]\s+/(.+)/\s*$")


def fetch() -> str:
    if os.path.exists(RAW) and os.path.getsize(RAW) > 100_000:
        print("using cached", RAW)
        return RAW
    print("downloading", URL, flush=True)
    urllib.request.urlretrieve(URL, RAW)
    print("saved", RAW)
    return RAW


def convert(path: str) -> None:
    d: dict[str, dict] = {}
    with gzip.open(path, "rt", encoding="utf-8") as f:
        for line in f:
            if line.startswith("#"):
                continue
            m = LINE_RE.match(line)
            if not m:
                continue
            simp = m.group(2)
            pinyin = m.group(3)
            senses = [s.strip() for s in m.group(4).split("/") if s.strip()]
            if not senses:
                continue
            entry = d.setdefault(simp, {"p": pinyin, "s": []})
            for s in senses:
                if s not in entry["s"]:
                    entry["s"].append(s)

    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    with open(OUT, "w", encoding="utf-8") as f:
        json.dump(d, f, ensure_ascii=False, separators=(",", ":"))
    print("entries:", len(d), "->", OUT)


if __name__ == "__main__":
    try:
        convert(fetch())
    except Exception as exc:  # noqa: BLE001
        print("CEDICT download/convert FAILED:", exc)
        print("keeping existing Data/cedict.json (mini fallback) if present")
        sys.exit(1)
