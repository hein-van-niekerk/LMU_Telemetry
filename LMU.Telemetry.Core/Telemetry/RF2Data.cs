using System.Runtime.InteropServices;

// Authoritative rFactor2 / Le Mans Ultimate shared-memory layout, matching
// TheIronWolf's rF2SharedMemoryMapPlugin (the plugin that publishes the
// $rFactor2SMMP_Telemetry$ and $rFactor2SMMP_Scoring$ memory maps).
//
// These structs are byte-exact to the plugin's rF2Data.cs (Pack = 4,
// CharSet.Ansi). Do not reorder fields or change array sizes — the marshaller
// relies on this layout to read the correct memory offsets.
namespace LMU.Telemetry.Core.Telemetry
{
    public static class RF2Constants
    {
        public const int MAX_MAPPED_VEHICLES = 128;
        public const string MM_TELEMETRY = "$rFactor2SMMP_Telemetry$";
        public const string MM_SCORING = "$rFactor2SMMP_Scoring$";
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct RF2Vec3
    {
        public double x, y, z;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 4)]
    public struct RF2Wheel
    {
        public double mSuspensionDeflection;
        public double mRideHeight;
        public double mSuspForce;
        public double mBrakeTemp;
        public double mBrakePressure;
        public double mRotation;
        public double mLateralPatchVel;
        public double mLongitudinalPatchVel;
        public double mLateralGroundVel;
        public double mLongitudinalGroundVel;
        public double mCamber;
        public double mLateralForce;
        public double mLongitudinalForce;
        public double mTireLoad;
        public double mGripFract;
        public double mPressure;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
        public double[] mTemperature;
        public double mWear;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] mTerrainName;
        public byte mSurfaceType;
        public byte mFlat;
        public byte mDetached;
        public byte mStaticUndeflectedRadius;
        public double mVerticalTireDeflection;
        public double mWheelYLocation;
        public double mToe;
        public double mTireCarcassTemperature;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
        public double[] mTireInnerLayerTemperature;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 24)]
        public byte[] mExpansion;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 4)]
    public struct RF2VehicleTelemetry
    {
        public int mID;
        public double mDeltaTime;
        public double mElapsedTime;
        public int mLapNumber;
        public double mLapStartET;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)]
        public byte[] mVehicleName;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)]
        public byte[] mTrackName;
        public RF2Vec3 mPos;
        public RF2Vec3 mLocalVel;
        public RF2Vec3 mLocalAccel;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
        public RF2Vec3[] mOri;
        public RF2Vec3 mLocalRot;
        public RF2Vec3 mLocalRotAccel;
        public int mGear;
        public double mEngineRPM;
        public double mEngineWaterTemp;
        public double mEngineOilTemp;
        public double mClutchRPM;
        public double mUnfilteredThrottle;
        public double mUnfilteredBrake;
        public double mUnfilteredSteering;
        public double mUnfilteredClutch;
        public double mFilteredThrottle;
        public double mFilteredBrake;
        public double mFilteredSteering;
        public double mFilteredClutch;
        public double mSteeringShaftTorque;
        public double mFront3rdDeflection;
        public double mRear3rdDeflection;
        public double mFrontWingHeight;
        public double mFrontRideHeight;
        public double mRearRideHeight;
        public double mDrag;
        public double mFrontDownforce;
        public double mRearDownforce;
        public double mFuel;
        public double mEngineMaxRPM;
        public byte mScheduledStops;
        public byte mOverheating;
        public byte mDetached;
        public byte mHeadlights;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
        public byte[] mDentSeverity;
        public double mLastImpactET;
        public double mLastImpactMagnitude;
        public RF2Vec3 mLastImpactPos;
        public double mEngineTorque;
        public int mCurrentSector;
        public byte mSpeedLimiter;
        public byte mMaxGears;
        public byte mFrontTireCompoundIndex;
        public byte mRearTireCompoundIndex;
        public double mFuelCapacity;
        public byte mFrontFlapActivated;
        public byte mRearFlapActivated;
        public byte mRearFlapLegalStatus;
        public byte mIgnitionStarter;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 18)]
        public byte[] mFrontTireCompoundName;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 18)]
        public byte[] mRearTireCompoundName;
        public byte mSpeedLimiterAvailable;
        public byte mAntiStallActivated;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public byte[] mUnused;
        public float mVisualSteeringWheelRange;
        public double mRearBrakeBias;
        public double mTurboBoostPressure;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
        public float[] mPhysicsToGraphicsOffset;
        public float mPhysicalSteeringWheelRange;
        public double mBatteryChargeFraction;
        public double mElectricBoostMotorTorque;
        public double mElectricBoostMotorRPM;
        public double mElectricBoostMotorTemperature;
        public double mElectricBoostWaterTemperature;
        public byte mElectricBoostMotorState;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 111)]
        public byte[] mExpansion;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
        public RF2Wheel[] mWheels;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 4)]
    public struct RF2Telemetry
    {
        public uint mVersionUpdateBegin;
        public uint mVersionUpdateEnd;
        public int mBytesUpdatedHint;
        public int mNumVehicles;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = RF2Constants.MAX_MAPPED_VEHICLES)]
        public RF2VehicleTelemetry[] mVehicles;
    }

    // Header-only view of the telemetry buffer, so we can read mNumVehicles
    // cheaply before deciding which vehicle struct(s) to marshal.
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct RF2TelemetryHeader
    {
        public uint mVersionUpdateBegin;
        public uint mVersionUpdateEnd;
        public int mBytesUpdatedHint;
        public int mNumVehicles;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 4)]
    public struct RF2ScoringInfo
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)]
        public byte[] mTrackName;
        public int mSession;
        public double mCurrentET;
        public double mEndET;
        public int mMaxLaps;
        public double mLapDist;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
        public byte[] pointer1;
        public int mNumVehicles;
        public byte mGamePhase;
        public sbyte mYellowFlagState;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
        public sbyte[] mSectorFlag;
        public byte mStartLight;
        public byte mNumRedLights;
        public byte mInRealtime;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public byte[] mPlayerName;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)]
        public byte[] mPlrFileName;
        public double mDarkCloud;
        public double mRaining;
        public double mAmbientTemp;
        public double mTrackTemp;
        public RF2Vec3 mWind;
        public double mMinPathWetness;
        public double mMaxPathWetness;
        public byte mGameMode;
        public byte mIsPasswordProtected;
        public ushort mServerPort;
        public uint mServerPublicIP;
        public int mMaxPlayers;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public byte[] mServerName;
        public float mStartET;
        public double mAvgPathWetness;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 200)]
        public byte[] mExpansion;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
        public byte[] pointer2;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 4)]
    public struct RF2VehicleScoring
    {
        public int mID;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public byte[] mDriverName;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)]
        public byte[] mVehicleName;
        public short mTotalLaps;
        public sbyte mSector;
        public sbyte mFinishStatus;
        public double mLapDist;
        public double mPathLateral;
        public double mTrackEdge;
        public double mBestSector1;
        public double mBestSector2;
        public double mBestLapTime;
        public double mLastSector1;
        public double mLastSector2;
        public double mLastLapTime;
        public double mCurSector1;
        public double mCurSector2;
        public short mNumPitstops;
        public short mNumPenalties;
        public byte mIsPlayer;
        public sbyte mControl;
        public byte mInPits;
        public byte mPlace;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public byte[] mVehicleClass;
        public double mTimeBehindNext;
        public int mLapsBehindNext;
        public double mTimeBehindLeader;
        public int mLapsBehindLeader;
        public double mLapStartET;
        public RF2Vec3 mPos;
        public RF2Vec3 mLocalVel;
        public RF2Vec3 mLocalAccel;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
        public RF2Vec3[] mOri;
        public RF2Vec3 mLocalRot;
        public RF2Vec3 mLocalRotAccel;
        public byte mHeadlights;
        public byte mPitState;
        public byte mServerScored;
        public byte mIndividualPhase;
        public int mQualification;
        public double mTimeIntoLap;
        public double mEstimatedLapTime;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 24)]
        public byte[] mPitGroup;
        public byte mFlag;
        public byte mUnderYellow;
        public byte mCountLapFlag;
        public byte mInGarageStall;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] mUpgradePack;
        public float mPitLapDist;
        public float mBestLapSector1;
        public float mBestLapSector2;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 48)]
        public byte[] mExpansion;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct RF2ScoringHeader
    {
        public uint mVersionUpdateBegin;
        public uint mVersionUpdateEnd;
        public int mBytesUpdatedHint;
    }
}
