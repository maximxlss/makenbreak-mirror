using UnityEngine;

public static class BuildInfo
{
    private const string ResourceName = "build_info";
    private const string UnknownTimestamp = "Unknown build";

    private static string _timestampUtc;

    public static string TimestampUtc
    {
        get
        {
            if (_timestampUtc != null)
            {
                return _timestampUtc;
            }

            TextAsset textAsset = Resources.Load<TextAsset>(ResourceName);
            _timestampUtc = textAsset ? textAsset.text.Trim() : UnknownTimestamp;
            return _timestampUtc;
        }
    }
}
