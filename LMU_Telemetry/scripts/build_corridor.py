#!/usr/bin/env python3
"""
build_corridor.py — Generic OSM-anchored track corridor builder.

Aligns an OSM polyline to game-GPS telemetry via Kabsch/Procrustes, resamples
to uniform arc-length, computes heading + smoothed curvature, and optionally
adds LeftWidth/RightWidth from tyre-track lateral offsets.

Usage
-----
  # Full pipeline (telemetry required for alignment + width):
  python build_corridor.py ref_lap.duckdb [extra_width.duckdb ...] \\
         --osm Reference/monza_stitched.json

  # Auto-detect OSM from track registry (track name read from DB metadata):
  python build_corridor.py ref_lap.duckdb

  # Override output path or track name:
  python build_corridor.py ref_lap.duckdb --output TrackMaps/Monza.json \\
         --track-name "Autodromo Nazionale Monza"

Algorithm
---------
1. Coarse arc-offset scan (100m steps): pairs OSM arc positions with telem
   LapDist using telem_LD = (OSM_arc + offset) % total.  Scores by mean
   distance from Procrustes-aligned OSM to nearest telem GPS point.
2. Fine scan (25m steps, +-200m around coarse best).
3. Final Procrustes with 70 pairs -> scale s, rotation R, translation t.
4. Resample aligned OSM to uniform spacing, compute heading + curvature.
5. Project telem onto centerline, bin signed lateral offsets -> widths.

Output schema (TrackMaps/*.json):
  TotalLength, GeneratedDateTime, TrackName, GeneratedFromLapCount
  Points[i]: {Position:{X,Y}, Heading, Curvature, LeftWidth, RightWidth}
"""

import argparse
import json
import math
import shutil
import sys
from datetime import datetime
from pathlib import Path

import duckdb
import numpy as np
from scipy.ndimage import gaussian_filter1d
from scipy.spatial import cKDTree

# ─────────────────────────────────────────────────────────────────────────────
# Configuration
# ─────────────────────────────────────────────────────────────────────────────

RESAMPLE_SPACING_M  = 3.0        # target arc-length between output points (m)
BIN_SIZE_M          = 2.0        # width-computation bin size (m)
METERS_PER_DEG      = 111_320.0  # matches C# ConvertGpsToMeters
COARSE_SCAN_STEP_M  = 100.0
FINE_SCAN_STEP_M    = 25.0
FINE_SCAN_HALF_M    = 200.0
N_PROCRUSTES_PAIRS  = 70


# ─────────────────────────────────────────────────────────────────────────────
# GPS -> metres
# ─────────────────────────────────────────────────────────────────────────────

def gps_to_meters_batch(lat, lon, ref_lat, ref_lon):
    lat_rad = math.radians(ref_lat)
    x = (lon - ref_lon) * METERS_PER_DEG * math.cos(lat_rad)
    y = (lat - ref_lat) * METERS_PER_DEG
    return x, y


# ─────────────────────────────────────────────────────────────────────────────
# DuckDB reading
# ─────────────────────────────────────────────────────────────────────────────

def read_track_name_from_db(db_path: str) -> str | None:
    try:
        conn = duckdb.connect(str(db_path), read_only=True)
        row = conn.execute("SELECT value FROM metadata WHERE key='TrackName' LIMIT 1").fetchone()
        conn.close()
        return row[0] if row else None
    except Exception:
        return None


def read_gps_and_lapdist(db_path):
    conn = duckdb.connect(str(db_path), read_only=True)
    lat = np.array([r[0] for r in conn.execute('SELECT value FROM "GPS Latitude"').fetchall()], dtype=float)
    lon = np.array([r[0] for r in conn.execute('SELECT value FROM "GPS Longitude"').fetchall()], dtype=float)
    ld  = np.array([r[0] for r in conn.execute('SELECT value FROM "Lap Dist"').fetchall()], dtype=float)
    conn.close()

    ref_lat, ref_lon = float(lat[0]), float(lon[0])
    x, y = gps_to_meters_batch(lat, lon, ref_lat, ref_lon)
    return x, y, ld, ref_lat, ref_lon


# ─────────────────────────────────────────────────────────────────────────────
# OSM loading
# ─────────────────────────────────────────────────────────────────────────────

def load_osm(osm_path):
    with open(osm_path, encoding="utf-8") as f:
        data = json.load(f)
    pts = np.asarray(data["points_local_xy_m"], dtype=float)
    diffs = np.diff(pts, axis=0)
    arc = np.concatenate([[0.0], np.cumsum(np.linalg.norm(diffs, axis=1))])
    return pts, arc, data


# ─────────────────────────────────────────────────────────────────────────────
# Kabsch / Procrustes similarity transform
# ─────────────────────────────────────────────────────────────────────────────

def solve_similarity(src, dst):
    p_bar, q_bar = src.mean(axis=0), dst.mean(axis=0)
    P, Q = src - p_bar, dst - q_bar
    U, S_diag, Vt = np.linalg.svd(P.T @ Q)
    det_sign = np.linalg.det(Vt.T @ U.T)
    D = np.diag([1.0, float(np.sign(det_sign))])
    R = Vt.T @ D @ U.T
    denom = float(np.sum(P ** 2))
    s = float(np.dot(S_diag, np.diag(D))) / denom if denom > 1e-9 else 1.0
    t = q_bar - s * (R @ p_bar)
    return s, R, t


def apply_transform(pts, s, R, t):
    return (s * (R @ pts.T)).T + t


# ─────────────────────────────────────────────────────────────────────────────
# Interpolation helpers
# ─────────────────────────────────────────────────────────────────────────────

def interp_osm(osm_pts, osm_arc, arc_s):
    total = float(osm_arc[-1])
    arc_s = float(arc_s) % total
    i = max(0, min(int(np.searchsorted(osm_arc, arc_s, side='right')) - 1, len(osm_pts) - 2))
    sl = float(osm_arc[i + 1] - osm_arc[i])
    if sl < 1e-9:
        return osm_pts[i].copy()
    return osm_pts[i] + ((arc_s - float(osm_arc[i])) / sl) * (osm_pts[i + 1] - osm_pts[i])


def interp_telem(telem_x, telem_y, telem_ld, target_ld, total_ld):
    target_ld = float(target_ld) % total_ld
    idx = int(np.argmin(np.abs(telem_ld - target_ld)))
    return float(telem_x[idx]), float(telem_y[idx])


# ─────────────────────────────────────────────────────────────────────────────
# Arc-offset scan + Procrustes
# ─────────────────────────────────────────────────────────────────────────────

def procrustes_at_offset(osm_pts, osm_arc, telem_x, telem_y, telem_ld,
                         arc_offset, n_pairs=N_PROCRUSTES_PAIRS):
    osm_total   = float(osm_arc[-1])
    telem_total = float(telem_ld.max())
    sample_arcs = np.linspace(0.0, osm_total, n_pairs, endpoint=False)
    src_pts, dst_pts = [], []
    for s in sample_arcs:
        osm_pt = interp_osm(osm_pts, osm_arc, s)
        tl = (s + arc_offset) % osm_total
        tx_, ty_ = interp_telem(telem_x, telem_y, telem_ld, tl, telem_total)
        src_pts.append(osm_pt)
        dst_pts.append([tx_, ty_])
    src_arr, dst_arr = np.array(src_pts), np.array(dst_pts)
    s, R, t = solve_similarity(src_arr, dst_arr)
    return s, R, t, apply_transform(osm_pts, s, R, t)


def find_best_arc_offset(osm_pts, osm_arc, telem_x, telem_y, telem_ld, telem_tree):
    osm_total = float(osm_arc[-1])
    best_mean, best_offset = float('inf'), 0.0

    n_coarse = int(round(osm_total / COARSE_SCAN_STEP_M))
    for i in range(n_coarse):
        _, _, _, aligned = procrustes_at_offset(
            osm_pts, osm_arc, telem_x, telem_y, telem_ld, i * COARSE_SCAN_STEP_M, n_pairs=30)
        mean_d = float(telem_tree.query(aligned)[0].mean())
        if mean_d < best_mean:
            best_mean, best_offset = mean_d, i * COARSE_SCAN_STEP_M

    fine_start = best_offset - FINE_SCAN_HALF_M
    for i in range(int(round(2 * FINE_SCAN_HALF_M / FINE_SCAN_STEP_M)) + 1):
        offset = (fine_start + i * FINE_SCAN_STEP_M) % osm_total
        _, _, _, aligned = procrustes_at_offset(
            osm_pts, osm_arc, telem_x, telem_y, telem_ld, offset)
        mean_d = float(telem_tree.query(aligned)[0].mean())
        if mean_d < best_mean:
            best_mean, best_offset = mean_d, offset

    s, R, t, aligned = procrustes_at_offset(
        osm_pts, osm_arc, telem_x, telem_y, telem_ld, best_offset)
    return best_offset, s, R, t, aligned, best_mean


# ─────────────────────────────────────────────────────────────────────────────
# Resampling
# ─────────────────────────────────────────────────────────────────────────────

def resample_polyline(pts, spacing):
    diffs    = np.diff(pts, axis=0)
    seg_lens = np.linalg.norm(diffs, axis=1)
    arc      = np.concatenate([[0.0], np.cumsum(seg_lens)])
    total    = arc[-1]
    n        = max(2, int(round(total / spacing)))
    targets  = np.linspace(0.0, total, n, endpoint=False)
    out      = np.empty((n, 2))
    for k, tgt in enumerate(targets):
        i = min(max(int(np.searchsorted(arc, tgt, side='right')) - 1, 0), len(seg_lens) - 1)
        sl = seg_lens[i]
        alpha = (tgt - arc[i]) / sl if sl > 1e-9 else 0.0
        out[k] = pts[i] + alpha * diffs[i]
    return out, total


# ─────────────────────────────────────────────────────────────────────────────
# Heading + curvature
# ─────────────────────────────────────────────────────────────────────────────

def heading_and_curvature(pts, smooth_sigma_m=20.0, spacing_m=3.0):
    n = len(pts)
    x, y = pts[:, 0], pts[:, 1]
    heading   = np.empty(n)
    curvature = np.zeros(n)
    for i in range(n):
        im1, ip1 = (i - 1) % n, (i + 1) % n
        heading[i] = math.atan2(float(y[ip1] - y[im1]), float(x[ip1] - x[im1]))
        if 0 < i < n - 1:
            dx  = (x[ip1] - x[im1]) / 2.0
            dy  = (y[ip1] - y[im1]) / 2.0
            ddx = x[ip1] - 2.0 * x[i] + x[im1]
            ddy = y[ip1] - 2.0 * y[i] + y[im1]
            num = abs(dx * ddy - dy * ddx)
            den = (dx * dx + dy * dy) ** 1.5
            curvature[i] = float(num / den) if den > 1e-9 else 0.0
    curvature = gaussian_filter1d(curvature, sigma=smooth_sigma_m / spacing_m, mode='wrap')
    return heading, curvature


# ─────────────────────────────────────────────────────────────────────────────
# Width computation
# ─────────────────────────────────────────────────────────────────────────────

def compute_width(osm_pts, osm_arc, telem_x, telem_y, telem_ld,
                  arc_offset, bin_size=BIN_SIZE_M):
    total_arc = float(osm_arc[-1])
    n_bins    = int(math.ceil(total_arc / bin_size))
    bin_min   = np.full(n_bins, np.inf)
    bin_max   = np.full(n_bins, -np.inf)
    bin_count = np.zeros(n_bins, dtype=int)

    diffs    = np.diff(osm_pts, axis=0)
    seg_lens = np.linalg.norm(diffs, axis=1)

    for xi, yi, ld in zip(telem_x.tolist(), telem_y.tolist(), telem_ld.tolist()):
        arc_pos = float((ld - arc_offset) % total_arc)
        arc_pos = max(0.0, min(arc_pos, total_arc - 1e-6))
        osm_idx = max(0, min(int(np.searchsorted(osm_arc, arc_pos, side='right')) - 1, len(seg_lens) - 1))
        sl = seg_lens[osm_idx]
        if sl < 1e-6:
            continue
        tang   = diffs[osm_idx] / sl
        normal = np.array([-tang[1], tang[0]])
        t_param = max(0.0, min((arc_pos - osm_arc[osm_idx]) / sl, 1.0))
        proj = osm_pts[osm_idx] + t_param * diffs[osm_idx]
        lat_offset = float(np.dot(np.array([xi, yi]) - proj, normal))
        bin_idx = int(arc_pos / bin_size)
        if 0 <= bin_idx < n_bins:
            bin_min[bin_idx] = min(bin_min[bin_idx], lat_offset)
            bin_max[bin_idx] = max(bin_max[bin_idx], lat_offset)
            bin_count[bin_idx] += 1

    arc_bins    = np.arange(n_bins) * bin_size + bin_size / 2.0
    raw_left    = np.where(bin_count > 0, bin_max,  np.nan)
    raw_right   = np.where(bin_count > 0, -bin_min, np.nan)
    left_widths  = np.where(raw_left  > 0, raw_left,  np.nan)
    right_widths = np.where(raw_right > 0, raw_right, np.nan)
    return arc_bins, left_widths, right_widths


def interp_width(arc_bins, widths, query_arc):
    valid_mask = ~np.isnan(widths)
    if not valid_mask.any():
        return None
    valid_arc, valid_w = arc_bins[valid_mask], widths[valid_mask]
    idx = int(np.searchsorted(valid_arc, query_arc))
    if idx == 0:
        return float(valid_w[0])
    if idx >= len(valid_arc):
        return float(valid_w[-1])
    alpha = (query_arc - valid_arc[idx - 1]) / (valid_arc[idx] - valid_arc[idx - 1])
    return float(valid_w[idx - 1] + alpha * (valid_w[idx] - valid_w[idx - 1]))


# ─────────────────────────────────────────────────────────────────────────────
# Main
# ─────────────────────────────────────────────────────────────────────────────

def main(argv=None):
    parser = argparse.ArgumentParser(
        description="Build OSM+telemetry corridor track map JSON (generic)")
    parser.add_argument("ref_lap_db",
                        help="DuckDB telemetry file used for centerline alignment")
    parser.add_argument("width_dbs", nargs="*",
                        help="Additional DuckDB files for width (default: same as ref_lap_db)")
    parser.add_argument("--osm", default=None,
                        help="Path to *_stitched.json OSM file. "
                             "If omitted, looked up in track_registry.py using the track name from the DB.")
    parser.add_argument("--output", default=None,
                        help="Output JSON path. Default: ../TrackMaps/{TrackName}.json")
    parser.add_argument("--track-name", default=None,
                        help="Override track name (default: read from DB metadata)")
    parser.add_argument("--spacing", type=float, default=RESAMPLE_SPACING_M,
                        help=f"Arc-length spacing of output points in metres (default {RESAMPLE_SPACING_M})")
    args = parser.parse_args(argv)

    script_dir  = Path(__file__).resolve().parent
    project_dir = script_dir.parent
    ref_dir     = project_dir / "Reference"
    maps_dir    = project_dir / "TrackMaps"

    # -- Determine track name ------------------------------------------------
    track_name = args.track_name or read_track_name_from_db(args.ref_lap_db)
    if not track_name:
        print("ERROR: could not read TrackName from DB metadata. Use --track-name.", file=sys.stderr)
        return 1

    # -- Determine OSM path --------------------------------------------------
    if args.osm:
        osm_path = Path(args.osm)
        osm_match = "explicit"
    else:
        sys.path.insert(0, str(script_dir))
        from track_registry import find_osm_for_track

        # Quick LapDist read for length-based disambiguation
        try:
            _conn = duckdb.connect(str(args.ref_lap_db), read_only=True)
            _ld_max = float(_conn.execute('SELECT MAX(value) FROM "Lap Dist"').fetchone()[0])
            _conn.close()
        except Exception:
            _ld_max = None

        osm_file, osm_match = find_osm_for_track(track_name, telem_length_m=_ld_max, ref_dir=ref_dir)

        if not osm_file:
            # ── Actionable failure message ───────────────────────────────────
            print()
            print("=" * 64)
            print("NO OSM MAPPING FOUND")
            print("=" * 64)
            print(f"  Game track name : \"{track_name}\"")
            if _ld_max:
                print(f"  Telem length    : {_ld_max:.0f} m")
            print()
            print("  Fix: add this track to Reference/name_corrections.json:")
            print(f'    "{track_name}": "<osm_filename_stitched.json>"')
            print()
            print("  Available OSM files in Reference/:")
            for f in sorted(ref_dir.glob("*_stitched.json")):
                print(f"    {f.name}")
            return 1

        osm_path = ref_dir / osm_file

    if not osm_path.exists():
        print(f"ERROR: OSM file not found: {osm_path}", file=sys.stderr)
        return 1

    # -- Determine output path -----------------------------------------------
    if args.output:
        output_path = Path(args.output)
    else:
        safe_name = "".join(c if c not in r'\/:*?"<>|' else "_" for c in track_name)
        output_path = maps_dir / f"{safe_name}.json"

    width_dbs = args.width_dbs or [args.ref_lap_db]

    print("=" * 64)
    print(f"Track corridor builder: {track_name}")
    print("=" * 64)
    print(f"OSM file   : {osm_path}  [{osm_match}]")
    print(f"Ref lap DB : {args.ref_lap_db}")
    if len(width_dbs) > 1 or width_dbs[0] != args.ref_lap_db:
        print(f"Width DBs  : {width_dbs}")
    print(f"Output     : {output_path}")
    print(f"Spacing    : {args.spacing} m")
    print()

    # -- Step 1: Load OSM ----------------------------------------------------
    print("[1] Loading OSM reference polyline ...")
    osm_pts, osm_arc, osm_meta = load_osm(str(osm_path))
    osm_total = float(osm_arc[-1])
    expected_km = osm_meta.get("length_km", osm_total / 1000.0)
    print(f"    {len(osm_pts)} points, arc = {osm_total:.1f} m  (expected ~{expected_km:.3f} km)")

    # -- Step 2: Load telemetry ----------------------------------------------
    print("[2] Loading telemetry GPS ...")
    ref_x, ref_y, ref_ld, ref_lat, ref_lon = read_gps_and_lapdist(args.ref_lap_db)
    print(f"    {len(ref_x)} rows, LapDist {ref_ld.min():.0f}->{ref_ld.max():.0f} m")
    print(f"    GPS ref: lat={ref_lat:.6f}, lon={ref_lon:.6f}")

    telem_pts_arr = np.stack([ref_x, ref_y], axis=1)
    telem_tree    = cKDTree(telem_pts_arr)

    # -- Step 3: Arc-offset scan + Procrustes --------------------------------
    print("[3] Scanning arc offset and solving similarity transform ...")
    best_offset, s, R, t, osm_aligned, mean_dist = find_best_arc_offset(
        osm_pts, osm_arc, ref_x, ref_y, ref_ld, telem_tree)

    theta_deg = math.degrees(math.atan2(float(R[1, 0]), float(R[0, 0])))
    aligned_total = float(np.linalg.norm(np.diff(osm_aligned, axis=0), axis=1).sum())
    print(f"    Arc offset : {best_offset:.0f} m")
    print(f"    Scale      : {s:.6f}")
    print(f"    Rotation   : {theta_deg:.3f} deg")
    print(f"    Translation: ({t[0]:.2f}, {t[1]:.2f}) m")
    print(f"    Mean dist  : {mean_dist:.1f} m")
    print(f"    Loop length: {aligned_total:.1f} m  (OSM reported {expected_km*1000:.0f} m)")

    # -- Step 4: Resample ----------------------------------------------------
    print(f"[4] Resampling to {args.spacing} m spacing ...")
    resampled, total_len = resample_polyline(osm_aligned, args.spacing)
    re_diffs = np.diff(resampled, axis=0)
    re_arc   = np.concatenate([[0.0], np.cumsum(np.linalg.norm(re_diffs, axis=1))])
    print(f"    {len(resampled)} points, total = {total_len:.1f} m")

    heading, curvature = heading_and_curvature(resampled, spacing_m=args.spacing)
    max_c_idx = int(np.argmax(curvature))
    print(f"    Curvature range: [{curvature.min():.5f}, {curvature.max():.5f}] 1/m "
          f"(peak at idx {max_c_idx}, arc {re_arc[max_c_idx]:.0f} m)")

    # -- Step 5: Corridor width ----------------------------------------------
    print("[5] Computing corridor width ...")
    aligned_diffs = np.diff(osm_aligned, axis=0)
    aligned_arc   = np.concatenate([[0.0], np.cumsum(np.linalg.norm(aligned_diffs, axis=1))])

    all_x, all_y, all_ld = [ref_x], [ref_y], [ref_ld]
    for db_path in width_dbs:
        if str(db_path) == str(args.ref_lap_db):
            continue
        wx, wy, wld, _, _ = read_gps_and_lapdist(db_path)
        all_x.append(wx); all_y.append(wy); all_ld.append(wld)

    all_x  = np.concatenate(all_x)
    all_y  = np.concatenate(all_y)
    all_ld = np.concatenate(all_ld)

    arc_bins, left_w, right_w = compute_width(
        osm_aligned, aligned_arc, all_x, all_y, all_ld,
        arc_offset=best_offset, bin_size=BIN_SIZE_M)

    n_left  = int(np.sum(~np.isnan(left_w)))
    n_right = int(np.sum(~np.isnan(right_w)))
    print(f"    {len(arc_bins)} bins — left: {100*n_left//len(arc_bins)}% coverage, "
          f"right: {100*n_right//len(arc_bins)}% coverage")

    # -- Step 6: Build output JSON -------------------------------------------
    print("[6] Building output ...")
    points_out = []
    for i in range(len(resampled)):
        arc_i = float(re_arc[i])
        points_out.append({
            "Position":   {"X": float(resampled[i, 0]), "Y": float(resampled[i, 1])},
            "Heading":    float(heading[i]),
            "Curvature":  float(curvature[i]),
            "LeftWidth":  interp_width(arc_bins, left_w,  arc_i),
            "RightWidth": interp_width(arc_bins, right_w, arc_i),
        })

    track_map = {
        "TrackName":             track_name,
        "TotalLength":           float(total_len),
        "GeneratedDateTime":     datetime.now().isoformat(),
        "GeneratedFromLapCount": 1 + len([d for d in width_dbs if d != args.ref_lap_db]),
        "ArcOffset":             float(best_offset),
        "Scale":                 float(s),
        "MeanAlignmentM":        float(mean_dist),
        "Points":                points_out,
        "Corners":               [],
    }

    # -- Step 7: Back up and write -------------------------------------------
    output_path.parent.mkdir(parents=True, exist_ok=True)
    if output_path.exists():
        bak = output_path.with_suffix(".json.bak")
        shutil.copy2(output_path, bak)
        print(f"    Backed up -> {bak.name}")

    with open(output_path, "w", encoding="utf-8") as f:
        json.dump(track_map, f, indent=2)
    print(f"    Written {len(points_out)} points -> {output_path}")

    # -- Verification --------------------------------------------------------
    print()
    print("=" * 64)
    print("VERIFICATION")
    print("=" * 64)
    print(f"  Track       : {track_name}")
    print(f"  Loop length : {total_len:.1f} m  (OSM reported {expected_km*1000:.0f} m, delta {total_len - expected_km*1000:.1f} m)")
    print(f"  Scale       : {s:.5f}  (expect ~1.0)")
    print(f"  Rotation    : {theta_deg:.2f} deg")
    print(f"  Arc offset  : {best_offset:.0f} m  (OSM[0] is {best_offset:.0f}m into game lap)")
    print(f"  Alignment   : {mean_dist:.1f} m mean OSM->telem dist")
    print(f"  Points      : {len(points_out)}")
    print(f"  Max curv    : {curvature.max():.5f} 1/m at arc {re_arc[max_c_idx]:.0f} m "
          f"(radius ~{1.0/curvature.max():.0f} m)" if curvature.max() > 1e-6 else "")
    print()
    print("Done.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
