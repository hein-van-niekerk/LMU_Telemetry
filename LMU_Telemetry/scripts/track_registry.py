"""
Track registry: maps LMU in-game track names to OSM stitched JSON files.

Matching priority:
  1. User corrections file  (Reference/name_corrections.json)  -- always wins
  2. Exact string match
  3. Case-insensitive + accent-normalised exact match
  4. Fuzzy word-overlap match with noise-word removal
  5. Length-based tiebreaker when multiple fuzzy candidates score equally

The "guessed" names below come from official circuit names; the real LMU
mTrackName values are confirmed only when you actually record telemetry at
that track.  When auto-generation fails the app logs the exact game name --
paste it into Reference/name_corrections.json to fix it permanently.
"""

from __future__ import annotations
import json
import re
import unicodedata
from pathlib import Path

# ---------------------------------------------------------------------------
# Registry: best-guess LMU mTrackName  ->  (osm_filename, osm_length_km)
# ---------------------------------------------------------------------------
# length_km is from the OSM source data (summary.csv) and used only as a
# tiebreaker when two name candidates score equally.

_ENTRIES: list[dict] = [
    # ── Confirmed ────────────────────────────────────────────────────────────
    {"lmu_name": "Circuit de Spa-Francorchamps",         "osm": "spa_osm_stitched.json",              "km": 6.995},

    # ── Best-guess primary names (update via name_corrections.json when confirmed) ──
    {"lmu_name": "Autodromo Nazionale Monza",            "osm": "monza_stitched.json",                "km": 5.794},
    {"lmu_name": "Silverstone Circuit",                  "osm": "silverstone_stitched.json",          "km": 5.881},
    {"lmu_name": "Circuit de Barcelona-Catalunya",       "osm": "barcelona_catalunya_stitched.json",  "km": 4.677},
    {"lmu_name": "Bahrain International Circuit",        "osm": "bahrain_endurance_stitched.json",    "km": 6.315},
    {"lmu_name": "Bahrain International Circuit Outer",  "osm": "bahrain_outer_stitched.json",        "km": 3.551},
    {"lmu_name": "Circuit of The Americas",              "osm": "cota_stitched.json",                 "km": 5.502},
    {"lmu_name": "Daytona International Speedway",       "osm": "daytona_road_course_stitched.json",  "km": 5.762},
    {"lmu_name": "Fuji Speedway",                        "osm": "fuji_stitched.json",                 "km": 4.554},
    {"lmu_name": "Autodromo Enzo e Dino Ferrari",        "osm": "imola_stitched.json",                "km": 4.904},
    {"lmu_name": "Autodromo Jose Carlos Pace",           "osm": "interlagos_stitched.json",           "km": 4.308},
    {"lmu_name": "WeatherTech Raceway Laguna Seca",      "osm": "lagunaseca_stitched.json",           "km": 3.601},
    {"lmu_name": "Circuit Bugatti",                      "osm": "lemans_bugatti_stitched.json",       "km": 4.164},
    {"lmu_name": "Lusail International Circuit",         "osm": "lusail_stitched.json",               "km": 5.426},
    {"lmu_name": "Circuit Paul Ricard",                  "osm": "paulricard_stitched.json",           "km": 5.764},
    {"lmu_name": "Autodromo Internacional do Algarve",   "osm": "portimao_stitched.json",             "km": 4.648},
    {"lmu_name": "Sebring International Raceway",        "osm": "sebring_stitched.json",              "km": 5.866},

    # ── Aliases: alternate / short names the game might use ──────────────────
    # These catch single-word names and common variants that fuzzy matching misses.
    {"lmu_name": "Monza",                                "osm": "monza_stitched.json",                "km": 5.794},
    {"lmu_name": "Silverstone",                          "osm": "silverstone_stitched.json",          "km": 5.881},
    {"lmu_name": "Barcelona",                            "osm": "barcelona_catalunya_stitched.json",  "km": 4.677},
    {"lmu_name": "COTA",                                 "osm": "cota_stitched.json",                 "km": 5.502},
    {"lmu_name": "Daytona",                              "osm": "daytona_road_course_stitched.json",  "km": 5.762},
    {"lmu_name": "Fuji",                                 "osm": "fuji_stitched.json",                 "km": 4.554},
    {"lmu_name": "Imola",                                "osm": "imola_stitched.json",                "km": 4.904},
    {"lmu_name": "Interlagos",                           "osm": "interlagos_stitched.json",           "km": 4.308},
    {"lmu_name": "Laguna Seca",                          "osm": "lagunaseca_stitched.json",           "km": 3.601},
    {"lmu_name": "Le Mans Bugatti",                      "osm": "lemans_bugatti_stitched.json",       "km": 4.164},
    {"lmu_name": "Le Mans Bugatti Circuit",              "osm": "lemans_bugatti_stitched.json",       "km": 4.164},
    {"lmu_name": "Bugatti Circuit",                      "osm": "lemans_bugatti_stitched.json",       "km": 4.164},
    {"lmu_name": "Lusail",                               "osm": "lusail_stitched.json",               "km": 5.426},
    {"lmu_name": "Paul Ricard",                          "osm": "paulricard_stitched.json",           "km": 5.764},
    {"lmu_name": "Portimao",                             "osm": "portimao_stitched.json",             "km": 4.648},
    {"lmu_name": "Portimão",                             "osm": "portimao_stitched.json",             "km": 4.648},
    {"lmu_name": "Sebring",                              "osm": "sebring_stitched.json",              "km": 5.866},
    {"lmu_name": "Bahrain",                              "osm": "bahrain_endurance_stitched.json",    "km": 6.315},
    {"lmu_name": "Spa",                                  "osm": "spa_osm_stitched.json",              "km": 6.995},
    {"lmu_name": "Spa-Francorchamps",                    "osm": "spa_osm_stitched.json",              "km": 6.995},
]

# Simple lookup maps built from the entries above
REGISTRY: dict[str, str] = {e["lmu_name"]: e["osm"] for e in _ENTRIES}
_BY_OSM:  dict[str, str] = {e["osm"]: e["lmu_name"] for e in _ENTRIES}
_LENGTHS: dict[str, float] = {e["osm"]: e["km"] for e in _ENTRIES}


# ---------------------------------------------------------------------------
# Internal helpers
# ---------------------------------------------------------------------------

_NOISE_WORDS = {
    "circuit", "circuits", "international", "raceway", "speedway",
    "autodromo", "autódromo", "nacional", "nazionale", "internazionale",
    "de", "del", "di", "do", "the", "of", "and", "e",
}


def _norm(s: str) -> set[str]:
    """Lowercase, strip accents, remove punctuation and noise words."""
    nfkd = unicodedata.normalize("NFKD", s)
    ascii_s = nfkd.encode("ascii", "ignore").decode("ascii")
    words = set(re.sub(r"[^a-z0-9 ]", " ", ascii_s.lower()).split())
    return words - _NOISE_WORDS


def _load_corrections(ref_dir: Path) -> dict[str, str]:
    """Load user-edited name_corrections.json if it exists."""
    path = ref_dir / "name_corrections.json"
    if not path.exists():
        return {}
    try:
        data = json.loads(path.read_text(encoding="utf-8"))
        return {k: v for k, v in data.get("corrections", {}).items() if isinstance(v, str)}
    except Exception:
        return {}


# ---------------------------------------------------------------------------
# Public API
# ---------------------------------------------------------------------------

def find_osm_for_track(
    lmu_name: str,
    telem_length_m: float | None = None,
    ref_dir: Path | None = None,
) -> tuple[str | None, str]:
    """
    Return (osm_filename, match_method) or (None, "no_match").

    match_method is one of:
      "corrections"          -- came from name_corrections.json
      "exact"                -- exact string match
      "normalised"           -- accent/case normalised exact match
      "fuzzy:<score>%"       -- word-overlap fuzzy match
      "fuzzy+length:<score>% -- fuzzy match disambiguated by track length
      "no_match"             -- nothing found
    """
    # 0. User corrections (highest priority)
    if ref_dir is None:
        ref_dir = Path(__file__).resolve().parent.parent / "Reference"
    corrections = _load_corrections(ref_dir)
    if lmu_name in corrections:
        return corrections[lmu_name], "corrections"

    # 1. Exact match
    if lmu_name in REGISTRY:
        return REGISTRY[lmu_name], "exact"

    # 2. Case-insensitive + accent-normalised exact match
    lmu_norm_words = _norm(lmu_name)
    for entry in _ENTRIES:
        if _norm(entry["lmu_name"]) == lmu_norm_words and lmu_norm_words:
            return entry["osm"], "normalised"

    # 3. Fuzzy word-overlap
    query = _norm(lmu_name)
    if not query:
        return None, "no_match"

    scored: list[tuple[float, dict]] = []
    for entry in _ENTRIES:
        reg_words = _norm(entry["lmu_name"])
        if not reg_words:
            continue
        overlap = len(query & reg_words) / len(query | reg_words)
        scored.append((overlap, entry))

    scored.sort(key=lambda x: x[0], reverse=True)

    if not scored or scored[0][0] < 0.35:
        return None, "no_match"

    best_score, best_entry = scored[0]

    # 4. If top two scores are within 0.05 of each other, use length as tiebreaker
    if telem_length_m is not None and len(scored) > 1:
        second_score = scored[1][0]
        if (best_score - second_score) < 0.05:
            # Pick the candidate whose OSM length is closest to telem length
            candidates = [(s, e) for s, e in scored if s >= best_score - 0.05]
            def length_delta(e: dict) -> float:
                return abs(e["km"] * 1000.0 - telem_length_m)
            candidates.sort(key=lambda x: length_delta(x[1]))
            best_entry = candidates[0][1]
            return best_entry["osm"], f"fuzzy+length:{best_score:.0%}"

    return best_entry["osm"], f"fuzzy:{best_score:.0%}"


def lmu_name_for_osm(osm_filename: str) -> str | None:
    return _BY_OSM.get(osm_filename)


def osm_length_km(osm_filename: str) -> float | None:
    return _LENGTHS.get(osm_filename)
