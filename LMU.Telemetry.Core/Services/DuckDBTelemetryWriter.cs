using System;
using System.Collections.Generic;
using DuckDB.NET.Data;
using LMU.Telemetry.Core.Models;

namespace LMU.Telemetry.Core.Services;

public static class DuckDBTelemetryWriter
{
    // Write frames to a new .duckdb file using the same schema the reader expects:
    // one table per channel with columns (ts DOUBLE, value DOUBLE/INTEGER),
    // plus a metadata table with (key VARCHAR, value VARCHAR).
    public static void Write(string filePath, IReadOnlyList<TelemetryFrame> frames,
                             string trackName = "", string carName = "")
    {
        if (frames == null || frames.Count == 0)
            throw new InvalidOperationException("No frames to save.");

        using var conn = new DuckDBConnection($"Data Source={filePath};");
        conn.Open();

        using var cmd = conn.CreateCommand();

        // Metadata
        cmd.CommandText = "CREATE TABLE metadata (key VARCHAR, value VARCHAR)";
        cmd.ExecuteNonQuery();
        Insert(cmd, "metadata", ("key", "TrackName"), ("value", trackName));
        Insert(cmd, "metadata", ("key", "CarName"),   ("value", carName));
        Insert(cmd, "metadata", ("key", "SavedAt"),   ("value", DateTime.UtcNow.ToString("o")));
        Insert(cmd, "metadata", ("key", "FrameCount"),("value", frames.Count.ToString()));

        // Continuous channels
        CreateDoubleTable(cmd, "World Pos X");
        CreateDoubleTable(cmd, "World Pos Y");
        CreateDoubleTable(cmd, "GPS Speed");
        CreateDoubleTable(cmd, "Throttle Pos");
        CreateDoubleTable(cmd, "Brake Pos");
        CreateDoubleTable(cmd, "Steering Pos");
        CreateDoubleTable(cmd, "Engine RPM");
        CreateDoubleTable(cmd, "Lap Dist");

        // Event/integer channels
        CreateIntTable(cmd, "Gear");
        CreateIntTable(cmd, "Lap");
        CreateIntTable(cmd, "Current Sector");

        // Bulk-insert using transactions for speed
        using var tx = conn.BeginTransaction();
        cmd.Transaction = tx as System.Data.Common.DbTransaction;

        foreach (var f in frames)
        {
            double t = f.Time;
            InsertDouble(cmd, "World Pos X",   t, f.PosX);
            InsertDouble(cmd, "World Pos Y",   t, f.PosY);
            InsertDouble(cmd, "GPS Speed",     t, f.Speed);
            InsertDouble(cmd, "Throttle Pos",  t, f.Throttle * 100.0);
            InsertDouble(cmd, "Brake Pos",     t, f.Brake * 100.0);
            InsertDouble(cmd, "Steering Pos",  t, f.Steering);
            InsertDouble(cmd, "Engine RPM",    t, f.Rpm);
            InsertDouble(cmd, "Lap Dist",      t, f.LapDistance);
            InsertInt(cmd,    "Gear",          t, f.Gear);
            InsertInt(cmd,    "Lap",           t, f.CurrentLap);
            InsertInt(cmd,    "Current Sector",t, f.Sector);
        }

        tx.Commit();
    }

    private static void CreateDoubleTable(DuckDBCommand cmd, string name)
    {
        cmd.CommandText = $"CREATE TABLE \"{name}\" (ts DOUBLE, value DOUBLE)";
        cmd.ExecuteNonQuery();
    }

    private static void CreateIntTable(DuckDBCommand cmd, string name)
    {
        cmd.CommandText = $"CREATE TABLE \"{name}\" (ts DOUBLE, value INTEGER)";
        cmd.ExecuteNonQuery();
    }

    private static void InsertDouble(DuckDBCommand cmd, string table, double ts, double value)
    {
        cmd.CommandText = $"INSERT INTO \"{table}\" VALUES ({ts:R}, {value:R})";
        cmd.ExecuteNonQuery();
    }

    private static void InsertInt(DuckDBCommand cmd, string table, double ts, int value)
    {
        cmd.CommandText = $"INSERT INTO \"{table}\" VALUES ({ts:R}, {value})";
        cmd.ExecuteNonQuery();
    }

    private static void Insert(DuckDBCommand cmd, string table, (string col, string val) a, (string col, string val) b)
    {
        cmd.CommandText = $"INSERT INTO {table} VALUES ('{Esc(a.val)}', '{Esc(b.val)}')";
        cmd.ExecuteNonQuery();
    }

    private static string Esc(string s) => s.Replace("'", "''");
}
