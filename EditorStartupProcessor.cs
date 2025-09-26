#if UNITY_IOS && UNITY_CLOUD_BUILD
using UnityEditor;
using UnityEngine;
using System;

[InitializeOnLoad]
public static class EditorStartupProcessor
{
    static EditorStartupProcessor()
    {
        string buildNumber = Environment.GetEnvironmentVariable("BUILD_NUMBER");
        if (!string.IsNullOrEmpty(buildNumber))
        {
            Debug.Log($"[EditorStartupProcessor] PlayerSettings.iOS.buildNumber = {PlayerSettings.iOS.buildNumber}");
            PlayerSettings.iOS.buildNumber = buildNumber;
            Debug.Log($"[EditorStartupProcessor] Set iOS build number to BUILD_NUMBER = {buildNumber}");
        }
        else
        {
            Debug.LogWarning("[EditorStartupProcessor] BUILD_NUMBER environment variable is not set.");
        }
    }
}
#endif
