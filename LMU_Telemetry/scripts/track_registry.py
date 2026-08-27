"""
Track registry: maps LMU in-game track names to OSM stitched JSON files.

The "lmu_name" values come from the game's rF2 shared memory mTrackName field,
which is stored in the DuckDB metadata table under key "TrackName".

HOW TO UPDATE: Record a session at a track, then read its DB with:
    python -c "import duckdb; c=duckdb.connect(r'path.duckdb', read_only=True); \
               print(c.execute(\"SELECT value FROM metadata WHERE key='TrackName'\").fetchone())"

The names marked # GUESS should be updated once you have actual telemetry for that track.
"""

# Maps LMU in-game track name -> OSM filename (in Reference/)
REGISTRY: dict[str, str] = {
    # ── Confirmed ─────────────────────────────────────────────────────────────
    "Circuit de Spa-Francorchamps":         "spa_osm_stitched.json",

    # ── Best-guess names (update from actual telemetry) ───────────────────────
    "Autodromo Nazionale Monza":            "monza_stitched.json",            # GUESS
    "Silverstone Circuit":                  "silverstone_stitched.json",       # GUESS
    "Circuit de Barcelona-Catalunya":       "barcelona_catalunya_stitched.json", # GUESS
    "Bahrain International Circuit":        "bahrain_endurance_stitched.json", # GUESS – endurance layout
    "Bahrain International Circuit Outer":  "bahrain_outer_stitched.json",     # GUESS
    "Circuit of The Americas":              "cota_stitched.json",              # GUESS
    "Daytona International Speedway":       "daytona_road_course_stitched.json", # GUESS
    "Fuji Speedway":                        "fuji_stitched.json",              # GUESS
    "Autodromo Enzo e Dino Ferrari":        "imola_stitched.json",             # GUESS
    "Autodromo Jose Carlos Pace":           "interlagos_stitched.json",        # GUESS
    "WeatherTech Raceway Laguna Seca":      "lagunaseca_stitched.json",        # GUESS
    "Circuit Bugatti":                      "lemans_bugatti_stitched.json",    # GUESS
    "Lusail International Circuit":         "lusail_stitched.json",            # GUESS
    "Circuit Paul Ricard":                  "paulricard_stitched.json",        # GUESS
    "Autodromo Internacional do Algarve":   "portimao_stitched.json",          # GUESS
    "Sebring International Raceway":        "sebring_stitched.json",           # GUESS
}

# Reverse map: OSM filename -> canonical LMU name
_REVERSE: dict[str, str] = {v: k for k, v in REGISTRY.items()}


def find_osm_for_track(lmu_name: str, fuzzy: bool = True) -> str | None:
    """
    Return the OSM filename for a given LMU track name, or None if unknown.
    With fuzzy=True, tries a case-insensitive and partial-word match as fallback.
    """
    # Exact match
    if lmu_name in REGISTRY:
        return REGISTRY[lmu_name]

    if not fuzzy:
        return None

    # Normalise: lower-case, strip punctuation
    import re
    def norm(s: str) -> str:
        return re.sub(r"[^a-z0-9 ]", " ", s.lower()).split()

    query_words = set(norm(lmu_name))
    best_score, best_osm = 0, None
    for reg_name, osm_file in REGISTRY.items():
        reg_words = set(norm(reg_name))
        overlap = len(query_words & reg_words) / max(len(query_words | reg_words), 1)
        if overlap > best_score:
            best_score = overlap
            best_osm = osm_file

    return best_osm if best_score > 0.4 else None


def lmu_name_for_osm(osm_filename: str) -> str | None:
    """Return the canonical LMU track name for a given OSM filename."""
    return _REVERSE.get(osm_filename)
