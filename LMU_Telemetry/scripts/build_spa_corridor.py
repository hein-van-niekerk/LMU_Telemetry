#!/usr/bin/env python3
"""
Build the Spa-Francorchamps OSM-calibrated track corridor JSON.

Replaces the old in-app track-map generator with a proper OSM-anchored map that
has physically-correct geometry and optional LeftWidth/RightWidth per point.

Usage
-----
  python build_spa_corridor.py <ref_lap.duckdb> [width1.duckdb ...] [options]

If only one DuckDB is provided it is used for both centerline and width.

Algorithm
---------
The OSM polyline begins at an arbitrary point on the circuit (Bus Stop area),
not at the game's LapDist=0 start line.  We find the arc offset automatically:

  1. Coarse scan (every 100m): for each candidate offset, pair OSM arc-positions
     with telemetry LapDist values and solve Kabsch/Procrustes.  Score by mean
     distance from transformed OSM to nearest telemetry GPS point.
  2. Fine scan (25m steps, +-200m around coarse best).
  3. Final Procrustes with 70 pairs at best offset -> scale, R, t.

Output schema (extends existing Points[] schema):
  Points[i].Position.{X, Y}   -- world metres, same frame as telemetry PosX/PosY
  Points[i].Heading            -- atan2 tangent, radians  (matches C# formula)
  Points[i].Curvature          -- abs(k) in 1/m           (matches C# formula)
  Points[i].LeftWidth          -- metres left of centreline  (null if no data)
  Points[i].RightWidth         -- metres right of centreline (null if no data)
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
from scipy.spatial import cKDTree
from scipy.ndimage import gaussian_filter1d

# ─────────────────────────────────────────────────────────────────────────────
# Configuration
# ─────────────────────────────────────────────────────────────────────────────

RESAMPLE_SPACING_M = 3.0        # target arc-length between output points (m)
BIN_SIZE_M = 2.0                # width-computation bin size (m)
METERS_PER_DEG = 111_320.0      # matches C# TelemetryDataProcessor.ConvertGpsToMeters

# Coarse arc-offset scan step (m).  Smaller = slower but more accurate.
COARSE_SCAN_STEP_M = 100.0
FINE_SCAN_STEP_M   = 25.0
FINE_SCAN_HALF_M   = 200.0

# Number of (OSM, telem) pairs for Procrustes at each candidate offset.
N_PROCRUSTES_PAIRS = 70


# ─────────────────────────────────────────────────────────────────────────────
# GPS -> metres (exact same formula as C# ConvertGpsToMeters)
# ─────────────────────────────────────────────────────────────────────────────

def gps_to_meters_batch(lat, lon, ref_lat, ref_lon):
    """Vectorised flat-earth GPS -> local metres, matching C# ConvertGpsToMeters."""
    lat_rad = math.radians(ref_lat)
    x = (lon - ref_lon) * METERS_PER_DEG * math.cos(lat_rad)
    y = (lat - ref_lat) * METERS_PER_DEG
    return x, y


# ─────────────────────────────────────────────────────────────────────────────
# DuckDB reading
# ─────────────────────────────────────────────────────────────────────────────

def read_gps_and_lapdist(db_path):
    """
    Returns (x_m, y_m, lap_dist, ref_lat, ref_lon) where x/y are metres in the
    coordinate frame whose origin is the first GPS sample of this recording.
    All arrays are parallel (same row count, same 10 Hz time base).
    """
    conn = duckdb.connect(str(db_path), read_only=True)
    lat = np.array([r[0] for r in conn.execute('SELECT value FROM "GPS Latitude"').fetchall()], dtype=float)
    lon = np.array([r[0] for r in conn.execute('SELECT value FROM "GPS Longitude"').fetchall()], dtype=float)
    ld  = np.array([r[0] for r in conn.execute('SELECT value FROM "Lap Dist"').fetchall()], dtype=float)
    conn.close()

    ref_lat = float(lat[0])
    ref_lon = float(lon[0])
    x, y = gps_to_meters_batch(lat, lon, ref_lat, ref_lon)
    return x, y, ld, ref_lat, ref_lon


# ─────────────────────────────────────────────────────────────────────────────
# OSM loading
# ─────────────────────────────────────────────────────────────────────────────

def load_osm(osm_path):
    """Returns (pts, arc) where pts is (N,2) ndarray and arc is cumulative arc-length."""
    with open(osm_path) as f:
        data = json.load(f)
    pts = np.asarray(data["points_local_xy_m"], dtype=float)  # (N, 2)
    diffs = np.diff(pts, axis=0)
    arc = np.concatenate([[0.0], np.cumsum(np.linalg.norm(diffs, axis=1))])
    return pts, arc


# ─────────────────────────────────────────────────────────────────────────────
# Kabsch / Procrustes similarity transform
# ─────────────────────────────────────────────────────────────────────────────

def solve_similarity(src, dst):
    """
    Find the 2D similarity transform T(p) = s*R*p + t minimising ||T(src_i)-dst_i||^2.
    Returns (s, R, t) where s is scalar scale, R is 2x2 rotation matrix, t is 2-vector.
    """
    p_bar = src.mean(axis=0)
    q_bar = dst.mean(axis=0)
    P = src - p_bar
    Q = dst - q_bar

    H = P.T @ Q
    U, S_diag, Vt = np.linalg.svd(H)

    det_sign = np.linalg.det(Vt.T @ U.T)
    D = np.diag([1.0, float(np.sign(det_sign))])
    R = Vt.T @ D @ U.T

    denom = float(np.sum(P ** 2))
    s = float(np.dot(S_diag, np.diag(D))) / denom if denom > 1e-9 else 1.0

    t = q_bar - s * (R @ p_bar)
    return s, R, t


def apply_transform(pts, s, R, t):
    """Apply similarity transform to an (N,2) array."""
    return (s * (R @ pts.T)).T + t


# ─────────────────────────────────────────────────────────────────────────────
# Helpers: interpolate OSM and telem at given arc / LapDist
# ─────────────────────────────────────────────────────────────────────────────

def interp_osm(osm_pts, osm_arc, arc_s):
    """Linear interpolation of OSM point at arc position arc_s (with wrapping)."""
    total = float(osm_arc[-1])
    arc_s = float(arc_s) % total
    i = int(np.searchsorted(osm_arc, arc_s, side='right')) - 1
    i = max(0, min(i, len(osm_pts) - 2))
    sl = float(osm_arc[i + 1] - osm_arc[i])
    if sl < 1e-9:
        return osm_pts[i].copy()
    alpha = (arc_s - float(osm_arc[i])) / sl
    return osm_pts[i] + alpha * (osm_pts[i + 1] - osm_pts[i])


def interp_telem(telem_x, telem_y, telem_ld, target_ld, total_ld):
    """Find telem XY at the LapDist closest to target_ld (mod total_ld)."""
    target_ld = float(target_ld) % total_ld
    idx = int(np.argmin(np.abs(telem_ld - target_ld)))
    return float(telem_x[idx]), float(telem_y[idx])


# ─────────────────────────────────────────────────────────────────────────────
# Arc-offset scan + Procrustes
# ─────────────────────────────────────────────────────────────────────────────

def procrustes_at_offset(osm_pts, osm_arc, telem_x, telem_y, telem_ld,
                         arc_offset, n_pairs=N_PROCRUSTES_PAIRS):
    """
    Build n_pairs (OSM, telem) correspondences using the relationship:
        telem_LapDist = (OSM_arc + arc_offset) mod OSM_total
    Solve and return (s, R, t, aligned_osm).
    """
    osm_total = float(osm_arc[-1])
    telem_total = float(telem_ld.max())

    sample_arcs = np.linspace(0.0, osm_total, n_pairs, endpoint=False)
    src_pts, dst_pts = [], []
    for s in sample_arcs:
        osm_pt = interp_osm(osm_pts, osm_arc, s)
        tl = (s + arc_offset) % osm_total
        tx_, ty_ = interp_telem(telem_x, telem_y, telem_ld, tl, telem_total)
        src_pts.append(osm_pt)
        dst_pts.append([tx_, ty_])

    src_arr = np.array(src_pts)
    dst_arr = np.array(dst_pts)
    s, R, t = solve_similarity(src_arr, dst_arr)
    aligned = apply_transform(osm_pts, s, R, t)
    return s, R, t, aligned


def find_best_arc_offset(osm_pts, osm_arc, telem_x, telem_y, telem_ld, telem_tree,
                         coarse_step=COARSE_SCAN_STEP_M,
                         fine_step=FINE_SCAN_STEP_M,
                         fine_half=FINE_SCAN_HALF_M):
    """
    Scan over arc offsets to find the value that minimises the mean distance from
    the transformed OSM to the nearest telemetry GPS point.
    Returns (best_offset, best_scale, best_R, best_t, best_aligned).
    """
    osm_total = float(osm_arc[-1])
    best_mean = float('inf')
    best_offset = 0.0

    # Coarse scan
    n_coarse = int(round(osm_total / coarse_step))
    for i in range(n_coarse):
        offset = i * coarse_step
        _, _, _, aligned = procrustes_at_offset(osm_pts, osm_arc, telem_x, telem_y,
                                                telem_ld, offset, n_pairs=30)
        dists, _ = telem_tree.query(aligned)
        mean_d = float(dists.mean())
        if mean_d < best_mean:
            best_mean = mean_d
            best_offset = offset

    # Fine scan
    fine_start = best_offset - fine_half
    fine_steps = int(round(2 * fine_half / fine_step)) + 1
    for i in range(fine_steps):
        offset = fine_start + i * fine_step
        offset = offset % osm_total
        _, _, _, aligned = procrustes_at_offset(osm_pts, osm_arc, telem_x, telem_y,
                                                telem_ld, offset, n_pairs=N_PROCRUSTES_PAIRS)
        dists, _ = telem_tree.query(aligned)
        mean_d = float(dists.mean())
        if mean_d < best_mean:
            best_mean = mean_d
            best_offset = offset

    # Final high-quality Procrustes at best offset
    s, R, t, aligned = procrustes_at_offset(osm_pts, osm_arc, telem_x, telem_y,
                                             telem_ld, best_offset, n_pairs=N_PROCRUSTES_PAIRS)
    return best_offset, s, R, t, aligned, best_mean


# ─────────────────────────────────────────────────────────────────────────────
# Resampling
# ─────────────────────────────────────────────────────────────────────────────

def resample_polyline(pts, spacing):
    """
    Resample a (possibly closed) polyline to uniform arc-length spacing.
    Returns (resampled_pts, total_arc).
    """
    diffs = np.diff(pts, axis=0)
    seg_lens = np.linalg.norm(diffs, axis=1)
    arc = np.concatenate([[0.0], np.cumsum(seg_lens)])
    total = arc[-1]

    n = max(2, int(round(total / spacing)))
    targets = np.linspace(0.0, total, n, endpoint=False)

    out = np.empty((n, 2))
    for k, tgt in enumerate(targets):
        i = int(np.searchsorted(arc, tgt, side='right')) - 1
        i = min(max(i, 0), len(seg_lens) - 1)
        sl = seg_lens[i]
        alpha = (tgt - arc[i]) / sl if sl > 1e-9 else 0.0
        out[k] = pts[i] + alpha * diffs[i]

    return out, total


# ─────────────────────────────────────────────────────────────────────────────
# Heading and curvature (matches TrackMapGenerator.cs CalculateHeadingAndCurvature)
# ─────────────────────────────────────────────────────────────────────────────

def heading_and_curvature(pts, smooth_sigma_m=20.0, spacing_m=3.0):
    """
    Heading = atan2 of central-difference tangent (radians).
    Curvature = |x'y'' - y'x''| / (x'^2 + y'^2)^1.5  (unsigned, 1/m).
    Both use wrap-around indexing for the closed loop.
    Exactly matches C# TrackMapGenerator.CalculateHeadingAndCurvature for heading.

    Curvature is additionally Gaussian-smoothed (sigma ~ 20m by default) because the
    OSM is sparsely sampled (~21m between original points), so the raw parametric
    curvature is concentrated in narrow spikes at the original vertices and zero
    everywhere else.  Smoothing spreads these into a physically realistic profile.
    """
    n = len(pts)
    x, y = pts[:, 0], pts[:, 1]
    heading   = np.empty(n)
    curvature = np.zeros(n)

    for i in range(n):
        im1 = (i - 1) % n
        ip1 = (i + 1) % n

        heading[i] = math.atan2(float(y[ip1] - y[im1]), float(x[ip1] - x[im1]))

        if 0 < i < n - 1:
            dx  = (x[ip1] - x[im1]) / 2.0
            dy  = (y[ip1] - y[im1]) / 2.0
            ddx = x[ip1] - 2.0 * x[i] + x[im1]
            ddy = y[ip1] - 2.0 * y[i] + y[im1]
            num = abs(dx * ddy - dy * ddx)
            den = (dx * dx + dy * dy) ** 1.5
            curvature[i] = float(num / den) if den > 1e-9 else 0.0

    # Gaussian smoothing: wrap-mode preserves continuity of the closed loop.
    sigma_pts = smooth_sigma_m / spacing_m
    curvature = gaussian_filter1d(curvature, sigma=sigma_pts, mode='wrap')
    return heading, curvature


# ─────────────────────────────────────────────────────────────────────────────
# Width computation
# ─────────────────────────────────────────────────────────────────────────────

def compute_width(osm_pts, osm_arc, telem_x, telem_y, telem_ld,
                  arc_offset, bin_size=BIN_SIZE_M):
    """
    For every telemetry point, convert LapDist to OSM arc position using:
        osm_arc_pos = (LapDist - arc_offset) mod total_arc
    Project onto OSM centerline tangent/normal, accumulate signed offsets.

    Returns (arc_bins_m, left_w, right_w) -- NaN where bin has no data.
    """
    total_arc = float(osm_arc[-1])
    n_bins = int(math.ceil(total_arc / bin_size))

    bin_min   = np.full(n_bins, np.inf)
    bin_max   = np.full(n_bins, -np.inf)
    bin_count = np.zeros(n_bins, dtype=int)

    diffs    = np.diff(osm_pts, axis=0)
    seg_lens = np.linalg.norm(diffs, axis=1)

    for xi, yi, ld in zip(telem_x.tolist(), telem_y.tolist(), telem_ld.tolist()):
        # Convert LapDist -> OSM arc position
        arc_pos = float((ld - arc_offset) % total_arc)
        arc_pos = max(0.0, min(arc_pos, total_arc - 1e-6))

        osm_idx = int(np.searchsorted(osm_arc, arc_pos, side='right')) - 1
        osm_idx = max(0, min(osm_idx, len(seg_lens) - 1))

        sl = seg_lens[osm_idx]
        if sl < 1e-6:
            continue

        tang   = diffs[osm_idx] / sl
        normal = np.array([-tang[1], tang[0]])  # 90 deg CCW (left = positive)

        t_param = (arc_pos - osm_arc[osm_idx]) / sl
        t_param = max(0.0, min(1.0, t_param))
        proj = osm_pts[osm_idx] + t_param * diffs[osm_idx]

        offset_vec  = np.array([xi, yi]) - proj
        lat_offset  = float(np.dot(offset_vec, normal))

        bin_idx = int(arc_pos / bin_size)
        if 0 <= bin_idx < n_bins:
            if lat_offset < bin_min[bin_idx]:
                bin_min[bin_idx] = lat_offset
            if lat_offset > bin_max[bin_idx]:
                bin_max[bin_idx] = lat_offset
            bin_count[bin_idx] += 1

    arc_bins = np.arange(n_bins) * bin_size + bin_size / 2.0
    # Clamp to >=0: a negative bin_max means the car never crossed the
    # centreline to the left, so we have no data for LeftWidth there.
    raw_left  = np.where(bin_count > 0, bin_max,  np.nan)
    raw_right = np.where(bin_count > 0, -bin_min, np.nan)
    left_widths  = np.where(raw_left  > 0, raw_left,  np.nan)
    right_widths = np.where(raw_right > 0, raw_right, np.nan)
    return arc_bins, left_widths, right_widths


def interp_width(arc_bins, widths, query_arc):
    """Linear interpolation of width at a given arc position. Returns None if no data."""
    valid_mask = ~np.isnan(widths)
    if not valid_mask.any():
        return None
    valid_arc = arc_bins[valid_mask]
    valid_w   = widths[valid_mask]
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
        description="Build Spa OSM+telemetry corridor track map JSON")
    parser.add_argument("ref_lap_db", help="DuckDB file used for the clean reference lap")
    parser.add_argument("width_dbs", nargs="*",
                        help="Extra DuckDB files used for width (defaults to ref_lap_db)")
    parser.add_argument("--osm", default=None,
                        help="Path to spa_osm_stitched.json (default: ../Reference/spa_osm_stitched.json)")
    parser.add_argument("--output", default=None,
                        help="Output JSON path (default: ../TrackMaps/Circuit de Spa-Francorchamps.json)")
    parser.add_argument("--spacing", type=float, default=RESAMPLE_SPACING_M,
                        help=f"Arc-length spacing of output points in metres (default {RESAMPLE_SPACING_M})")
    args = parser.parse_args(argv)

    script_dir  = Path(__file__).resolve().parent
    project_dir = script_dir.parent

    osm_path    = Path(args.osm) if args.osm else project_dir / "Reference" / "spa_osm_stitched.json"
    output_path = Path(args.output) if args.output else (
        project_dir / "TrackMaps" / "Circuit de Spa-Francorchamps.json")
    width_dbs   = args.width_dbs or [args.ref_lap_db]

    print("=" * 60)
    print("Spa OSM Corridor Builder")
    print("=" * 60)
    print(f"OSM reference : {osm_path}")
    print(f"Ref lap DB    : {args.ref_lap_db}")
    print(f"Width DBs     : {width_dbs}")
    print(f"Output        : {output_path}")
    print(f"Spacing       : {args.spacing} m")
    print()

    # -- Step 1: Load OSM ---------------------------------------------------
    print("[1] Loading OSM reference polyline ...")
    osm_pts, osm_arc = load_osm(str(osm_path))
    osm_total = float(osm_arc[-1])
    print(f"    {len(osm_pts)} points, total arc = {osm_total:.1f} m")

    # -- Step 2: Load telemetry (all rows, all segments) --------------------
    print("[2] Loading telemetry GPS ...")
    ref_x, ref_y, ref_ld, ref_lat, ref_lon = read_gps_and_lapdist(args.ref_lap_db)
    print(f"    {len(ref_x)} rows, LapDist {ref_ld.min():.0f}->{ref_ld.max():.0f} m")
    print(f"    GPS reference: lat={ref_lat:.6f}, lon={ref_lon:.6f}")

    telem_pts_arr = np.stack([ref_x, ref_y], axis=1)
    telem_tree    = cKDTree(telem_pts_arr)

    # -- Step 3: Find arc offset + similarity transform ---------------------
    print("[3] Scanning arc offset and solving similarity transform ...")
    print(f"    Coarse scan step: {COARSE_SCAN_STEP_M:.0f} m, fine: {FINE_SCAN_STEP_M:.0f} m +-{FINE_SCAN_HALF_M:.0f} m")

    best_offset, s, R, t, osm_aligned, mean_dist = find_best_arc_offset(
        osm_pts, osm_arc, ref_x, ref_y, ref_ld, telem_tree)

    theta_deg = math.degrees(math.atan2(float(R[1, 0]), float(R[0, 0])))
    print(f"    Best arc offset: {best_offset:.0f} m")
    print(f"    Scale:           {s:.6f}")
    print(f"    Rotation:        {theta_deg:.3f} deg")
    print(f"    Translation:     ({t[0]:.2f}, {t[1]:.2f}) m")
    print(f"    Mean dist (OSM->telem): {mean_dist:.1f} m")

    # Aligned arc (should equal osm_total * s)
    aligned_diffs = np.diff(osm_aligned, axis=0)
    aligned_arc   = np.concatenate([[0.0], np.cumsum(np.linalg.norm(aligned_diffs, axis=1))])
    aligned_total = float(aligned_arc[-1])
    print(f"    Aligned loop length: {aligned_total:.1f} m  (real Spa GP: 7004 m)")

    # -- Step 4: Resample ---------------------------------------------------
    print(f"[4] Resampling to {args.spacing} m spacing ...")
    resampled, total_len = resample_polyline(osm_aligned, args.spacing)
    print(f"    {len(resampled)} output points, total = {total_len:.1f} m")

    re_diffs = np.diff(resampled, axis=0)
    re_arc   = np.concatenate([[0.0], np.cumsum(np.linalg.norm(re_diffs, axis=1))])

    heading, curvature = heading_and_curvature(resampled, spacing_m=args.spacing)

    # -- Step 5: Corridor width --------------------------------------------
    print("[5] Computing corridor width ...")
    all_x, all_y, all_ld = [ref_x], [ref_y], [ref_ld]
    for db_path in width_dbs:
        if str(db_path) == str(args.ref_lap_db):
            continue  # already loaded
        wx, wy, wld, _, _ = read_gps_and_lapdist(db_path)
        all_x.append(wx)
        all_y.append(wy)
        all_ld.append(wld)

    all_x  = np.concatenate(all_x)
    all_y  = np.concatenate(all_y)
    all_ld = np.concatenate(all_ld)
    print(f"    {len(all_x)} total telem points from {len(width_dbs)} file(s)")

    # Width is computed in the ALIGNED OSM frame (osm_aligned, aligned_arc)
    arc_bins, left_w, right_w = compute_width(
        osm_aligned, aligned_arc, all_x, all_y, all_ld,
        arc_offset=best_offset, bin_size=BIN_SIZE_M)

    n_data = int(np.sum(~np.isnan(left_w)))
    print(f"    {len(arc_bins)} bins, {n_data} with data ({100.0 * n_data / len(arc_bins):.0f}% coverage)")

    # -- Step 6: Build output JSON -----------------------------------------
    print("[6] Building output JSON ...")
    points_out = []
    for i in range(len(resampled)):
        arc_i = float(re_arc[i])
        lw = interp_width(arc_bins, left_w, arc_i)
        rw = interp_width(arc_bins, right_w, arc_i)
        points_out.append({
            "Position":  {"X": float(resampled[i, 0]), "Y": float(resampled[i, 1])},
            "Heading":   float(heading[i]),
            "Curvature": float(curvature[i]),
            "LeftWidth":  lw,
            "RightWidth": rw,
        })

    track_map = {
        "Points":                points_out,
        "Corners":               [],
        "GeneratedFromLapCount": len(width_dbs),
        "TotalLength":           float(total_len),
        "GeneratedDateTime":     datetime.now().isoformat(),
        "TrackName":             "Circuit de Spa-Francorchamps",
    }

    # -- Step 7: Back up and write ----------------------------------------
    print("[7] Writing output ...")
    output_path.parent.mkdir(parents=True, exist_ok=True)
    if output_path.exists():
        bak = output_path.with_suffix(".json.bak")
        shutil.copy2(output_path, bak)
        print(f"    Backed up -> {bak.name}")

    with open(output_path, "w", encoding="utf-8") as f:
        json.dump(track_map, f, indent=2)
    print(f"    Written {len(points_out)} points -> {output_path}")

    # -- Verification -------------------------------------------------------
    print()
    print("=" * 60)
    print("VERIFICATION")
    print("=" * 60)
    print(f"  Loop length:  {total_len:.1f} m  (real Spa GP = 7004 m, delta = {total_len - 7004:.1f} m)")
    print(f"  Scale factor: {s:.5f}  (expect ~1.0)")
    print(f"  Rotation:     {theta_deg:.2f} deg")
    print(f"  Arc offset:   {best_offset:.0f} m  (OSM start is {best_offset:.0f}m into game lap)")
    print(f"  Mean GPS-OSM alignment: {mean_dist:.1f} m")
    print(f"  Points:       {len(points_out)}")

    # Curvature spot-checks.
    # Convert game LapDist -> resampled arc index.  Use max-in-window because the
    # OSM vertices (where curvature lives) may be +-30m from the landmark position.
    WINDOW_M = 60.0
    window_pts = int(round(WINDOW_M / args.spacing))

    def lapdist_to_resampled_idx(lapdist):
        arc_s = (float(lapdist) - best_offset) % aligned_total
        return int(np.argmin(np.abs(re_arc - arc_s)))

    def max_curv_near(lapdist):
        center = lapdist_to_resampled_idx(lapdist)
        lo = max(0, center - window_pts)
        hi = min(len(curvature), center + window_pts + 1)
        return float(curvature[lo:hi].max())

    print()
    print(f"  Curvature spot-checks (1/m, max in +-{WINDOW_M:.0f}m window):")
    for name, ld_m, expect in [
        ("SF line      ", 0,    "expect ~0"),
        ("La Source    ", 285,  "expect high  ~0.02-0.04"),
        ("AfterKemmel  ", 2296, ""),
        ("AnnoyingCorner", 5041,""),
    ]:
        curv = max_curv_near(ld_m)
        print(f"    {name}: LapDist={ld_m:5.0f}m  max_curv={curv:.5f}  {expect}")

    # Global curvature stats
    print(f"  Curvature range: [{curvature.min():.5f}, {curvature.max():.5f}] 1/m")

    print()
    print("  Width spot-checks (m):")
    for name, ld_m in [("La Source    ", 285), ("AfterKemmel  ", 2296), ("AnnoyingCorner", 5041)]:
        arc_s = (float(ld_m) - best_offset) % aligned_total
        lw_ = interp_width(arc_bins, left_w, arc_s)
        rw_ = interp_width(arc_bins, right_w, arc_s)
        l_str = f"L={lw_:.1f}" if lw_ is not None else "L=n/a"
        r_str = f"R={rw_:.1f}" if rw_ is not None else "R=n/a"
        tot = (lw_ or 0) + (rw_ or 0)
        print(f"    {name}: LapDist={ld_m:5.0f}m  {l_str}  {r_str}  total={tot:.1f}")

    print()
    print("Done.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
