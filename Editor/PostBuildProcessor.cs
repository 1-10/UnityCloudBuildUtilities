#if UNITY_IOS
using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using Path = System.IO.Path;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;
using UnityEngine;

public class PostBuildProcessor
{
    [PostProcessBuild(9999)]
    public static void OnPostProcessBuild(BuildTarget buildTarget, string pathToBuiltProject)
    {
        Debug.Log($"[PostBuildProcessor]OnPostProcessBuild({buildTarget}, {pathToBuiltProject})");

        if (buildTarget != BuildTarget.iOS)
        {
            Debug.Log("[PostBuildProcessor] Not an iOS build. Skipping post process.");
            return;
        }

        // Apply CFBundleVersion from Cloud Build env
#if UNITY_CLOUD_BUILD
        // 表示用バージョン（マーケティング用）
        string bundleVersion = Environment.GetEnvironmentVariable("BUNDLE_VERSION");
        Debug.Log($"[PostBuildProcessor] BUNDLE_VERSION = {bundleVersion}");
        if (!string.IsNullOrEmpty(bundleVersion))
        {
            Debug.Log($"[PostBuildProcessor] PlayerSettings.bundleVersion = {PlayerSettings.bundleVersion}");
            PlayerSettings.bundleVersion = bundleVersion;
            Debug.Log($"[PostBuildProcessor] Set CFBundleShortVersionString = {bundleVersion}");
        }
        else
        {
            Debug.LogWarning("[PostBuildProcessor] BUNDLE_VERSION is undefined.");
        }
#endif

        string projPath = PBXProject.GetPBXProjectPath(pathToBuiltProject);
        PBXProject project = new PBXProject();
        project.ReadFromFile(projPath);

#if UNITY_2019_3_OR_NEWER
        string targetGuid = project.GetUnityMainTargetGuid();
        string frameworkTargetGuid = project.GetUnityFrameworkTargetGuid();
#else
        string targetGuid = project.TargetGuidByName(PBXProject.GetUnityTargetName());
        string frameworkTargetGuid = project.TargetGuidByName("UnityFramework");
#endif

        // Disable Bitcode
        project.SetBuildProperty(frameworkTargetGuid, "ENABLE_BITCODE", "NO");
        Debug.Log("[PostBuildProcessor] Set ENABLE_BITCODE = NO");

        // Disable embedding Swift standard libraries (fix App Store submission error)
        project.SetBuildProperty(frameworkTargetGuid, "ALWAYS_EMBED_SWIFT_STANDARD_LIBRARIES", "NO");
        Debug.Log("[PostBuildProcessor] Set ALWAYS_EMBED_SWIFT_STANDARD_LIBRARIES = NO");

        // Enable C/ObjC Modules
        project.SetBuildProperty(frameworkTargetGuid, "CLANG_ENABLE_MODULES", "YES");
        Debug.Log("[PostBuildProcessor] Set CLANG_ENABLE_MODULES = YES");

        // Apply to main target as well
        project.SetBuildProperty(targetGuid, "ENABLE_BITCODE", "NO");
        project.SetBuildProperty(targetGuid, "ALWAYS_EMBED_SWIFT_STANDARD_LIBRARIES", "NO");
        project.SetBuildProperty(targetGuid, "CLANG_ENABLE_MODULES", "YES");

        // Link flags
        project.AddBuildProperty(targetGuid, "OTHER_LDFLAGS", "-ObjC");
        project.AddBuildProperty(targetGuid, "OTHER_LDFLAGS", "-lc++");
        project.AddBuildProperty(targetGuid, "OTHER_LDFLAGS", "-lz");

        // Add required frameworks
        List<string> frameworks = new List<string>()
        {
            "AVFoundation.framework",
            "CoreMedia.framework",
            "SystemConfiguration.framework"
        };
        foreach (var framework in frameworks)
        {
            project.AddFrameworkToProject(targetGuid, framework, false);
        }

        project.WriteToFile(projPath);

        // Log contents of Frameworks folder (for debugging disallowed Frameworks)
        string frameworksPath = Path.Combine(pathToBuiltProject, "Frameworks");
        if (Directory.Exists(frameworksPath))
        {
            foreach (string file in Directory.GetFiles(frameworksPath, "*", SearchOption.AllDirectories))
            {
                Debug.Log($"[PostBuildProcessor] Frameworks contains: {file}");
            }
        }

        //TestFlightにアップすると毎回表示される「輸出コンプライアンスがありません」の設定を省略する
        _SetExportCompliance(pathToBuiltProject);
    }

    private static void _SetExportCompliance(string pathToBuiltProject)
    {
        Debug.Log($"[PostBuildProcessor]_SetExportCompliance({pathToBuiltProject})");

        string plistPath = Path.Combine(pathToBuiltProject, "Info.plist");
        PlistDocument plist = new PlistDocument();
        plist.ReadFromFile(plistPath);

        plist.root.SetBoolean("ITSAppUsesNonExemptEncryption", false);
        File.WriteAllText(plistPath, plist.WriteToString());

        Debug.Log("[PostBuildProcessor] Set ITSAppUsesNonExemptEncryption = false in Info.plist");
    }
}
#endif