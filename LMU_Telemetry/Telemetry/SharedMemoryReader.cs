using System;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using LMU_Telemetry.Models;

namespace LMU_Telemetry.Telemetry
{
    // FR-1: Read telemetry from LMU shared memory
    public class SharedMemoryReader : IDisposable
    {
        private const string SharedMemoryName = "$rFactor2SMMP_Telemetry$";
        private MemoryMappedFile? _memoryMappedFile;
        private MemoryMappedViewAccessor? _accessor;
        private bool _isConnected;

        public bool IsConnected => _isConnected;

        public bool Connect()
        {
            try
            {
                // Try to open the LMU/rF2 shared memory
                _memoryMappedFile = MemoryMappedFile.OpenExisting(SharedMemoryName, MemoryMappedFileRights.Read);
                _accessor = _memoryMappedFile.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
                _isConnected = true;
                return true;
            }
            catch (FileNotFoundException)
            {
                // Game not running or shared memory not available
                _isConnected = false;
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
            _accessor?.Dispose();
            _accessor = null;
            _memoryMappedFile?.Dispose();
            _memoryMappedFile = null;
            _isConnected = false;
        }

        public TelemetryFrame? ReadTelemetry()
        {
            if (!_isConnected || _accessor == null)
                return null;

            try
            {
                // Read the RF2Telemetry struct from shared memory
                _accessor.Read(0, out RF2Telemetry telemetry);

                // Check if telemetry is valid (player exists)
                if (telemetry.mNumVehicles <= 0)
                    return null;

                // Get player vehicle (index 0 is always the player)
                var vehicle = telemetry.mVehicles[0];

                // Convert to our TelemetryFrame format
                return new TelemetryFrame
                {
                    Time = telemetry.mCurrentET,
                    PosX = vehicle.mPos.x,
                    PosY = vehicle.mPos.z, // Use Z as Y for 2D track map
                    Speed = (float)Math.Sqrt(vehicle.mLocalVel.x * vehicle.mLocalVel.x + 
                                            vehicle.mLocalVel.y * vehicle.mLocalVel.y + 
                                            vehicle.mLocalVel.z * vehicle.mLocalVel.z) * 3.6f, // m/s to km/h
                    Throttle = vehicle.mUnfilteredThrottle,
                    Brake = vehicle.mUnfilteredBrake,
                    Steering = vehicle.mUnfilteredSteering,
                    Gear = vehicle.mGear,
                    Rpm = vehicle.mEngineRPM
                };
            }
            catch (Exception)
            {
                // Handle read errors (game might have closed)
                _isConnected = false;
                return null;
            }
        }

        public void Dispose()
        {
            Disconnect();
        }

        // LMU/rFactor2 shared memory structures (simplified)
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct Vec3
        {
            public float x, y, z;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct RF2VehicleTelemetry
        {
            public int mID;
            public float mDeltaTime;
            public float mElapsedTime;
            public int mLapNumber;
            public float mLapStartET;

            public Vec3 mPos;
            public Vec3 mLocalVel;
            public Vec3 mLocalAccel;

            public Vec3 mOriX;
            public Vec3 mOriY;
            public Vec3 mOriZ;
            public Vec3 mLocalRot;
            public Vec3 mLocalRotAccel;

            public int mGear;
            public float mEngineRPM;
            public float mEngineWaterTemp;
            public float mEngineOilTemp;
            public float mClutchRPM;

            public float mUnfilteredThrottle;
            public float mUnfilteredBrake;
            public float mUnfilteredSteering;
            public float mUnfilteredClutch;

            public float mFilteredThrottle;
            public float mFilteredBrake;
            public float mFilteredSteering;
            public float mFilteredClutch;

            public float mSteeringShaftTorque;
            public float mFront3rdDeflection;
            public float mRear3rdDeflection;

            public float mFrontWingHeight;
            public float mFrontRideHeight;
            public float mRearRideHeight;

            public float mDrag;
            public float mFrontDownforce;
            public float mRearDownforce;

            public float mFuel;
            public float mEngineMaxRPM;
            public byte mScheduledStops;
            public byte mOverheating;
            public byte mDetached;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 1)]
            public byte[] mHeadlights;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
            public byte[] mDentSeverity;

            public float mLastImpactET;
            public float mLastImpactMagnitude;
            public Vec3 mLastImpactPos;

            // Wheels (simplified, would have 4 wheel structs here)
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 512)]
            public byte[] mWheelsData; // Placeholder for wheel telemetry
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct RF2Telemetry
        {
            public int mVersionUpdateBegin;
            public int mVersionUpdateEnd;

            public int mBytesUpdatedHint;
            public double mCurrentET;
            public int mLapNumber;
            public float mLapStartET;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)]
            public byte[] mVehicleName;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)]
            public byte[] mTrackName;

            public Vec3 mPos;
            public Vec3 mLocalVel;
            public Vec3 mLocalAccel;

            public Vec3 mOriX;
            public Vec3 mOriY;
            public Vec3 mOriZ;
            public Vec3 mLocalRot;
            public Vec3 mLocalRotAccel;

            public int mGamePhase;
            public int mSession;
            public int mMaxLaps;
            public double mLapDist;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
            public byte[] mResultsStream;

            public int mNumVehicles;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 128)]
            public RF2VehicleTelemetry[] mVehicles;
        }
    }
}
