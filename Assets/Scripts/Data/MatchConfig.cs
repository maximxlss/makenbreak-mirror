using System;
using System.Globalization;
using UnityEngine;

public readonly struct MatchConfig
{
    public const float DefaultRoundDurationSeconds = 180f;
    public const uint DefaultPointsToWin = 100;

    public readonly float RoundDurationSeconds;
    public readonly uint PointsToWin;

    public MatchConfig(float roundDurationSeconds, uint pointsToWin)
    {
        RoundDurationSeconds = Mathf.Max(1f, roundDurationSeconds);
        PointsToWin = Math.Max(1u, pointsToWin);
    }

    public static MatchConfig Default => new(DefaultRoundDurationSeconds, DefaultPointsToWin);

    public static bool TryParseRoundDuration(string value, out float seconds)
    {
        return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out seconds) ||
               float.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out seconds);
    }

    public static bool TryParsePointsToWin(string value, out uint pointsToWin)
    {
        return uint.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out pointsToWin) ||
               uint.TryParse(value, NumberStyles.Integer, CultureInfo.CurrentCulture, out pointsToWin);
    }

    public string RoundDurationString()
    {
        return RoundDurationSeconds.ToString("0.##", CultureInfo.InvariantCulture);
    }

    public string PointsToWinString()
    {
        return PointsToWin.ToString(CultureInfo.InvariantCulture);
    }
}

public static class MatchLaunchConfig
{
    private static MatchConfig _config = MatchConfig.Default;

    public static MatchConfig Config => _config;

    public static void Set(MatchConfig config)
    {
        _config = config;
    }

    public static void Reset()
    {
        _config = MatchConfig.Default;
    }
}
