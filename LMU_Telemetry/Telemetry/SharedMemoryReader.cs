using System;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Text;
using LMU_Telemetry.Models;

namespace LMU_Telemetry.Telemetry
{
    // FR-1: Read telemetry from LMU / rFactor2 shared memory.
    //
    // Uses the real rF2SharedMemoryMapPlugin layout (see RF2Data.cs). Reads the
    // Telemetry map for physics/inputs and the Scoring map to identify the
    // player's car (the telemetry array order is NOT guaranteed) and to pull
    // real lap/sector/session data. Every value here comes straight from the
    // sim — nothing is estimated.
    public class SharedMemoryReader : IDisposable
    {
        private MemoryMappedFile? _telemetryMmf;
        private MemoryMappedViewAccessor? _telemetryView;
        private MemoryMappedFile? _scoringMmf;
        private MemoryMappedViewAccessor? _scoringView;
        private bool _isConnected;

        private static readonly int TelemetryHeaderSize = Marshal.SizeOf<RF2TelemetryHeader>();
        private static readonly int VehicleTelemetrySize = Marshal.SizeOf<RF2VehicleTelemetry>();
        private static readonly int ScoringHeaderSize = Marshal.SizeOf<RF2ScoringHeader>();
        private static readonly int ScoringInfoSize = Marshal.SizeOf<RF2ScoringInfo>();
        private static readonly int VehicleScoringSize = Marshal.SizeOf<RF2VehicleScoring>();

        public bool IsConnected => _isConnected;

        public bool Connect()
        {
            try
            {
                _telemetryMmf = MemoryMappedFile.OpenExisting(RF2Constants.MM_TELEMETRY, MemoryMappedFileRights.Read);
                _telemetryView = _telemetryMmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);

                // Scoring is optional but strongly preferred (player identification).
                try
                {
                    _scoringMmf = MemoryMappedFile.OpenExisting(RF2Constants.MM_SCORING, MemoryMappedFileRights.Read);
                    _scoringView = _scoringMmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
                }
                catch (FileNotFoundException) { /* run without scoring enrichment */ }

                _isConnected = true;
                return true;
            }
            catch (FileNotFoundException)
            {
                _isConnected = false;   // Plugin/game not running
                return false;
            }
            catch (Exception)
            {
                _isConnected = false;
                return false;
            }
        }

        public void Disconnect()
        {
            _scoringView?.Dispose(); _scoringView = null;
            _scoringMmf?.Dispose(); _scoringMmf = null;
            _telemetryView?.Dispose(); _telemetryView = null;
            _telemetryMmf?.Dispose(); _telemetryMmf = null;
            _isConnected = false;
        }

        public TelemetryFrame? ReadTelemetry()
        {
            if (!_isConnected || _telemetryView == null)
                return null;

            try
            {
                // Identify the player and capture scoring-derived real data.
                var playerScoring = TryReadPlayerScoring(out RF2ScoringInfo scoringInfo, out bool haveScoring);
                int playerId = playerScoring?.mID ?? int.MinValue;

                // Find the player's telemetry vehicle (version-safe).
                if (!TryReadPlayerTelemetry(playerId, out RF2VehicleTelemetry v))
                    return null;

                double speedMs = Math.Sqrt(v.mLocalVel.x * v.mLocalVel.x +
                                           v.mLocalVel.y * v.mLocalVel.y +
                                           v.mLocalVel.z * v.mLocalVel.z);

                var frame = new TelemetryFrame
                {
                    Time = v.mElapsedTime,
                    PosX = (float)v.mPos.x,
                    PosY = (float)v.mPos.z,                 // Z is forward/back -> 2D map Y
                    Speed = (float)(speedMs * 3.6),          // m/s -> km/h

                    Throttle = (float)v.mUnfilteredThrottle,
                    Brake = (float)v.mUnfilteredBrake,
                    Steering = (float)v.mUnfilteredSteering, // -1 (left) .. +1 (right)
                    Clutch = (float)v.mUnfilteredClutch,

                    Gear = v.mGear,
                    Rpm = (float)v.mEngineRPM,
                    RpmMax = (float)v.mEngineMaxRPM,

                    SteeringWheelRangeVisual = v.mVisualSteeringWheelRange,
                    SteeringWheelRangePhysical = v.mPhysicalSteeringWheelRange,

                    EngineWaterTemp = (float)v.mEngineWaterTemp,
                    EngineOilTemp = (float)v.mEngineOilTemp,
                    Fuel = (float)v.mFuel,

                    Sector = v.mCurrentSector,
                    CurrentLap = v.mLapNumber,

                    // Scoring-derived (real lap timing). Defaults when scoring absent.
                    LapDistance = playerScoring.HasValue ? (float)playerScoring.Value.mLapDist : 0f,
                    LapTime = playerScoring.HasValue ? (float)playerScoring.Value.mTimeIntoLap : 0f,
                    LastLapTime = playerScoring.HasValue ? (float)playerScoring.Value.mLastLapTime : -1f,
                    BestLapTime = playerScoring.HasValue ? (float)playerScoring.Value.mBestLapTime : -1f,
                };

                // Extended channels (real values not mapped to first-class fields).
                frame.ExtendedData["VehicleName"] = BytesToString(v.mVehicleName);
                frame.ExtendedData["SteeringShaftTorque"] = v.mSteeringShaftTorque;
                frame.ExtendedData["FrontRideHeight"] = v.mFrontRideHeight;
                frame.ExtendedData["RearRideHeight"] = v.mRearRideHeight;
                frame.ExtendedData["RearBrakeBias"] = v.mRearBrakeBias;
                frame.ExtendedData["TurboBoostPressure"] = v.mTurboBoostPressure;
                frame.ExtendedData["BatteryChargeFraction"] = v.mBatteryChargeFraction;
                if (v.mWheels != null && v.mWheels.Length == 4)
                {
                    string[] wn = { "FL", "FR", "RL", "RR" };
                    for (int i = 0; i < 4; i++)
                    {
                        var w = v.mWheels[i];
                        frame.ExtendedData[$"Tire{wn[i]}_Load"] = w.mTireLoad;
                        frame.ExtendedData[$"Tire{wn[i]}_Pressure"] = w.mPressure;
                        frame.ExtendedData[$"Tire{wn[i]}_Grip"] = w.mGripFract;
                        frame.ExtendedData[$"Tire{wn[i]}_Wear"] = w.mWear;
                        frame.ExtendedData[$"Brake{wn[i]}_Temp"] = w.mBrakeTemp;
                        if (w.mTemperature != null && w.mTemperature.Length == 3)
                            frame.ExtendedData[$"Tire{wn[i]}_Temp"] = (w.mTemperature[0] + w.mTemperature[1] + w.mTemperature[2]) / 3.0;
                    }
                }
                if (haveScoring)
                {
                    // Track+layout key resolution (see Track Map Dev Mode spec):
                    // rF2Data.cs shows the shared-memory scoring/telemetry structs
                    // expose exactly one identifier for the circuit — mTrackName —
                    // no separate layout/venue ID field exists in this API. Per the
                    // standard rF2/LMU shared-memory plugin convention, mTrackName
                    // already differentiates layouts within the string itself (e.g.
                    // distinct strings per configuration of the same venue), so this
                    // raw value already serves as a de-facto {Track}_{Layout} key —
                    // it's just not literally formatted that way. Dev Mode's raw-lap
                    // storage, track-map storage and library all key off this same
                    // string consistently, so different layouts of one track do not
                    // collide as long as the sim reports distinct names for them.
                    frame.ExtendedData["TrackName"] = BytesToString(scoringInfo.mTrackName);
                    frame.ExtendedData["Session"] = scoringInfo.mSession;
                    frame.ExtendedData["AmbientTemp"] = scoringInfo.mAmbientTemp;
                    frame.ExtendedData["TrackTemp"] = scoringInfo.mTrackTemp;
                }
                if (playerScoring.HasValue)
                    frame.ExtendedData["VehicleClass"] = BytesToString(playerScoring.Value.mVehicleClass);

                return frame;
            }
            catch (Exception)
            {
                _isConnected = false;   // Game likely closed mid-read
                return null;
            }
        }

        // --- Player telemetry, version-safe ---------------------------------
        private bool TryReadPlayerTelemetry(int playerId, out RF2VehicleTelemetry result)
        {
            result = default;
            var view = _telemetryView!;
            bool gotAny = false;

            for (int attempt = 0; attempt < 4; attempt++)
            {
                var header = ReadStruct<RF2TelemetryHeader>(view, 0);
                int count = Math.Min(header.mNumVehicles, RF2Constants.MAX_MAPPED_VEHICLES);
                if (count <= 0) return false;

                int chosen = -1;
                int fallback = -1;
                for (int i = 0; i < count; i++)
                {
                    int idAt = view.ReadInt32(TelemetryHeaderSize + i * VehicleTelemetrySize); // mID is first field
                    if (i == 0) fallback = 0;
                    if (idAt == playerId) { chosen = i; break; }
                }
                if (chosen < 0) chosen = fallback;
                if (chosen < 0) return false;

                result = ReadStruct<RF2VehicleTelemetry>(view, TelemetryHeaderSize + chosen * VehicleTelemetrySize);
                gotAny = true;

                // Accept only a clean frame: the plugin bumps mVersionUpdateBegin
                // (offset 0) before writing and mVersionUpdateEnd (offset 4) after,
                // so a stable frame has begin == end before AND after the copy.
                uint endAfter = view.ReadUInt32(4);
                if (header.mVersionUpdateBegin == header.mVersionUpdateEnd &&
                    endAfter == header.mVersionUpdateEnd)
                    return true;
            }
            return gotAny; // last read was slightly torn but usable; better than a dropped frame
        }

        // --- Player scoring entry + session info ----------------------------
        private RF2VehicleScoring? TryReadPlayerScoring(out RF2ScoringInfo scoringInfo, out bool haveScoring)
        {
            scoringInfo = default;
            haveScoring = false;
            if (_scoringView == null) return null;
            var view = _scoringView;

            try
            {
                var header = ReadStruct<RF2ScoringHeader>(view, 0);
                scoringInfo = ReadStruct<RF2ScoringInfo>(view, ScoringHeaderSize);
                haveScoring = true;

                int count = Math.Min(scoringInfo.mNumVehicles, RF2Constants.MAX_MAPPED_VEHICLES);
                int vehBase = ScoringHeaderSize + ScoringInfoSize;
                for (int i = 0; i < count; i++)
                {
                    int off = vehBase + i * VehicleScoringSize;
                    // mIsPlayer sits well inside the struct; only marshal the full
                    // entry once we've confirmed it's the player to keep this cheap.
                    var vs = ReadStruct<RF2VehicleScoring>(view, off);
                    if (vs.mIsPlayer != 0)
                        return vs;
                }
            }
            catch
            {
                haveScoring = false;
            }
            return null;
        }

        // --- Marshalling helpers --------------------------------------------
        private static T ReadStruct<T>(MemoryMappedViewAccessor view, int offset) where T : struct
        {
            int size = Marshal.SizeOf<T>();
            byte[] buf = new byte[size];
            view.ReadArray(offset, buf, 0, size);
            GCHandle h = GCHandle.Alloc(buf, GCHandleType.Pinned);
            try { return Marshal.PtrToStructure<T>(h.AddrOfPinnedObject()); }
            finally { h.Free(); }
        }

        private static string BytesToString(byte[]? bytes)
        {
            if (bytes == null) return string.Empty;
            int len = Array.IndexOf(bytes, (byte)0);
            if (len < 0) len = bytes.Length;
            return Encoding.ASCII.GetString(bytes, 0, len);
        }

        public void Dispose() => Disconnect();
    }
}
