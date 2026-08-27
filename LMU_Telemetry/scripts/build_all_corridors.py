#!/usr/bin/env python3
"""
build_all_corridors.py — Batch corridor builder for all available tracks.

Scans a telemetry directory for .duckdb files, reads the TrackName from each
DB's metadata table, looks up the matching OSM file in track_registry.py,
and runs build_corridor.py for every track that has both data sources.

Usage
-----
  # Use default telemetry dir (~/Downloads/Telemetry):
  python build_all_corridors.py

  # Specify telemetry directory:
  python build_all_corridors.py --telem-dir "C:/Users/User/Downloads/Telemetry"

  # Re-run a specific track only:
  python build_all_corridors.py --track "Circuit de Spa-Francorchamps"

  # Dry run (show what would be processed):
  python build_all_corridors.py --dry-run

When new telemetry arrives for an unrecognised track, you will see:
  [SKIP] MyTrackName -- no OSM mapping found (add to track_registry.py)

Update track_registry.py with the exact in-game name printed there to enable
that track for future runs.
"""

import argparse
import subprocess
import sys
from pathlib import Path

import duckdb


def read_track_name_from_db(db_path: Path) -> str | None:
    try:
        conn = duckdb.connect(str(db_path), read_only=True)
        row = conn.execute("SELECT value FROM metadata WHERE key='TrackName' LIMIT 1").fetchone()
        conn.close()
        return row[0] if row else None
    except Exception:
        return None


def find_best_db_per_track(telem_dir: Path) -> dict[str, list[Path]]:
    """
    Scan telem_dir for .duckdb files, group by TrackName.
    Returns {track_name: [db_path, ...]} sorted newest-first within each group.
    """
    groups: dict[str, list[Path]] = {}
    for db_path in sorted(telem_dir.glob("*.duckdb")):
        name = read_track_name_from_db(db_path)
        if name:
            groups.setdefault(name, []).append(db_path)
    return groups


def main(argv=None):
    script_dir  = Path(__file__).resolve().parent
    project_dir = script_dir.parent
    default_telem_dir = Path.home() / "Downloads" / "Telemetry"

    parser = argparse.ArgumentParser(
        description="Batch OSM corridor builder for all tracks with telemetry")
    parser.add_argument("--telem-dir", default=str(default_telem_dir),
                        help=f"Directory containing .duckdb telemetry files "
                             f"(default: {default_telem_dir})")
    parser.add_argument("--track", default=None,
                        help="Process only this track name (exact LMU name)")
    parser.add_argument("--dry-run", action="store_true",
                        help="Show what would run without actually running it")
    parser.add_argument("--spacing", type=float, default=3.0,
                        help="Arc-length spacing in metres (default 3.0)")
    args = parser.parse_args(argv)

    telem_dir = Path(args.telem_dir)
    if not telem_dir.exists():
        print(f"ERROR: telemetry directory not found: {telem_dir}", file=sys.stderr)
        print("  -> Record sessions in LMU and save the .duckdb files there.", file=sys.stderr)
        return 1

    # Import registry
    sys.path.insert(0, str(script_dir))
    from track_registry import REGISTRY, find_osm_for_track

    ref_dir  = project_dir / "Reference"
    maps_dir = project_dir / "TrackMaps"

    print("=" * 64)
    print("LMU Track Corridor Batch Builder")
    print("=" * 64)
    print(f"Telemetry dir : {telem_dir}")
    print(f"OSM dir       : {ref_dir}")
    print(f"Output dir    : {maps_dir}")
    print()

    # Discover available telemetry
    track_dbs = find_best_db_per_track(telem_dir)

    if not track_dbs:
        print("No .duckdb files with TrackName metadata found.")
        print(f"Save telemetry recordings to: {telem_dir}")
        return 0

    print(f"Found telemetry for {len(track_dbs)} track(s):")
    for name, dbs in sorted(track_dbs.items()):
        print(f"  {name}  ({len(dbs)} file(s))")
    print()

    # Show registry status for all known tracks
    print("Registry status:")
    osm_files_present = {f.name for f in ref_dir.glob("*.json")}
    for lmu_name, osm_file in sorted(REGISTRY.items()):
        has_telem  = lmu_name in track_dbs
        has_osm    = osm_file in osm_files_present
        map_exists = (maps_dir / (lmu_name + ".json")).exists()
        status = []
        if has_telem: status.append("telem")
        if has_osm:   status.append("OSM")
        if map_exists: status.append("map(exists)")
        print(f"  {'[READY]' if has_telem and has_osm else '[WAIT] ':8s} {lmu_name}  ({', '.join(status) or 'no data'})")
    print()

    # Process
    results = {"ok": [], "skip": [], "error": []}

    for track_name, dbs in sorted(track_dbs.items()):
        if args.track and args.track.lower() != track_name.lower():
            continue

        osm_file = find_osm_for_track(track_name)
        if not osm_file:
            print(f"[SKIP] {track_name}")
            print(f"       -- no OSM mapping found (add to track_registry.py)")
            results["skip"].append(track_name)
            continue

        osm_path = ref_dir / osm_file
        if not osm_path.exists():
            print(f"[SKIP] {track_name}")
            print(f"       -- OSM file missing: {osm_path}")
            results["skip"].append(track_name)
            continue

        # Use newest DB as ref lap; all DBs for width
        ref_db    = dbs[-1]
        width_dbs = dbs

        safe_name   = "".join(c if c not in r'\/:*?"<>|' else "_" for c in track_name)
        output_path = maps_dir / f"{safe_name}.json"

        cmd = [
            sys.executable,
            str(script_dir / "build_corridor.py"),
            str(ref_db),
            *[str(d) for d in width_dbs if d != ref_db],
            "--osm", str(osm_path),
            "--output", str(output_path),
            "--track-name", track_name,
            "--spacing", str(args.spacing),
        ]

        print(f"[RUN] {track_name}")
        print(f"      ref_db  : {ref_db.name}")
        print(f"      osm     : {osm_file}")
        print(f"      output  : {output_path.name}")
        if len(width_dbs) > 1:
            print(f"      width DBs: {len(width_dbs)} files")

        if args.dry_run:
            print("      [dry-run: skipped]")
            continue

        print()
        result = subprocess.run(cmd, capture_output=False)
        print()

        if result.returncode == 0:
            results["ok"].append(track_name)
        else:
            print(f"  ERROR: build_corridor.py exited with code {result.returncode}")
            results["error"].append(track_name)

    # Summary
    print("=" * 64)
    print("SUMMARY")
    print("=" * 64)
    if results["ok"]:
        print(f"  Built  ({len(results['ok'])}): " + ", ".join(results["ok"]))
    if results["skip"]:
        print(f"  Skipped({len(results['skip'])}): " + ", ".join(results["skip"]))
    if results["error"]:
        print(f"  Errors ({len(results['error'])}): " + ", ".join(results["error"]))

    pending = [n for n in REGISTRY if n not in track_dbs]
    if pending:
        print()
        print(f"  Waiting for telemetry ({len(pending)} tracks):")
        for name in sorted(pending):
            print(f"    - {name}")
        print()
        print("  -> Record a session at each track and save the .duckdb file to:")
        print(f"     {telem_dir}")

    return 1 if results["error"] else 0


if __name__ == "__main__":
    sys.exit(main())
