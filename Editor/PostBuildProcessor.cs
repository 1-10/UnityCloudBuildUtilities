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
using DG.Tweening.Plugins.Core.PathCore;

public class PostBuildProcessor
{
    //※チームIDはApple Developerで確認してください。
    public static string teamID = "YBL5BK26BK";

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
        string buildNumber = Environment.GetEnvironmentVariable("BUILD_NUMBER");
        Debug.Log($"[PostBuildProcessor] BUILD_NUMBER = {buildNumber}");
        if (!string.IsNullOrEmpty(buildNumber))
        {
            Debug.Log($"[PostBuildProcessor] PlayerSettings.iOS.buildNumber = {PlayerSettings.iOS.buildNumber}");
            PlayerSettings.iOS.buildNumber = buildNumber;
            Debug.Log($"[PostBuildProcessor] Set iOS Build Number to {buildNumber}");
        }

        // 現在の Build 番号取得
        //string buildNumber = PlayerSettings.iOS.buildNumber;
        //if (int.TryParse(buildNumber, out int number))
        //{
        //    number++;
        //    PlayerSettings.iOS.buildNumber = number.ToString();
        //    Debug.Log($"[AutoIncrementBuild]Build Number を自動更新しました: {buildNumber} → {number}");
        //}

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

        string appName = PlayerSettings.productName;
        // Setup Entitlements for Push/Background
        //_CreateEntitlements(appName, pathToBuiltProject, targetGuid);
        //_SetCapabilities(appName, pathToBuiltProject, targetGuid, teamID);
    }

    private static void _CreateEntitlements(string appName, string pathToBuiltProject, string targetGuid)
    {
        Debug.Log($"[PostBuildProcessor]_CreateEntitlements({appName}, {pathToBuiltProject})");

        string entitlementsDir = Path.Combine(pathToBuiltProject, "Unity-iPhone");
        if (!Directory.Exists(entitlementsDir))
        {
            Directory.CreateDirectory(entitlementsDir);
            Debug.Log($"[PostBuildProcessor] Created directory: {entitlementsDir}");
        }

        string entitlementsPath = Path.Combine(pathToBuiltProject, $"Unity-iPhone/{appName}.entitlements");

        var doc = new XmlDocument();
        var decl = doc.CreateXmlDeclaration("1.0", "UTF-8", null);
        doc.AppendChild(decl);
        var plist = doc.CreateElement("plist");
        plist.SetAttribute("version", "1.0");
        var dict = doc.CreateElement("dict");
        plist.AppendChild(dict);
        doc.AppendChild(plist);

        var key = doc.CreateElement("key");
        key.InnerText = "aps-environment";
        dict.AppendChild(key);
        var stringNode = doc.CreateElement("string");
        stringNode.InnerText = "development"; // or "production"
        dict.AppendChild(stringNode);

        doc.Save(entitlementsPath);

        string projPath = PBXProject.GetPBXProjectPath(pathToBuiltProject);
        PBXProject project = new PBXProject();
        project.ReadFromFile(projPath);

        string relativeEntitlementsPath = $"Unity-iPhone/{appName}.entitlements";
        project.SetBuildProperty(targetGuid, "CODE_SIGN_ENTITLEMENTS", relativeEntitlementsPath);
        project.WriteToFile(projPath);
    }

    private static void _SetCapabilities(string appName, string pathToBuiltProject, string targetGuid, string teamId)
    {
        Debug.Log($"[PostBuildProcessor]_SetCapabilities({appName}, {pathToBuiltProject}, {targetGuid}, {teamId})");

        string projPath = PBXProject.GetPBXProjectPath(pathToBuiltProject);
        PBXProject project = new PBXProject();
        project.ReadFromFile(projPath);
        
        string entitlementsRelativePath = $"Unity-iPhone/{appName}.entitlements";
        project.AddCapability(targetGuid, PBXCapabilityType.PushNotifications, entitlementsRelativePath, false);
        project.AddCapability(targetGuid, PBXCapabilityType.BackgroundModes);
        project.WriteToFile(projPath);

        // Inject Team ID and capabilities
        string[] lines = File.ReadAllLines(projPath);
        List<string> newLines = new List<string>();
        bool inserted = false;

        foreach (string line in lines)
        {
            newLines.Add(line);
            if (!inserted && line.Contains("TargetAttributes = {"))
            {
                newLines.Add($"\t\t\t\t{targetGuid} = {{");
                newLines.Add($"\t\t\t\t\tDevelopmentTeam = {teamId};");
                newLines.Add($"\t\t\t\t\tSystemCapabilities = {{");
                newLines.Add($"\t\t\t\t\t\tcom.apple.BackgroundModes = {{ enabled = 1; }};");
                newLines.Add($"\t\t\t\t\t\tcom.apple.Push = {{ enabled = 1; }};");
                newLines.Add($"\t\t\t\t\t}};");
                newLines.Add("\t\t\t\t};");
                inserted = true;
            }
        }

        File.WriteAllLines(projPath, newLines.ToArray());
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

































//#if UNITY_IOS
//using System.Collections.Generic;
//using System.IO;
//using System.Xml;
//using Path = System.IO.Path;
//using UnityEditor;
//using UnityEditor.Callbacks;
//using UnityEditor.iOS.Xcode;
//using UnityEngine;

//public class PostBuildProcessor
//{
//    //public static void DumpBuildFolderContents(string path)
//    //{
//    //    Debug.Log($"[PostBuildProcessor]DumpBuildFolderContents({path})");

//    //    foreach (var dir in Directory.GetDirectories(path, "*", SearchOption.AllDirectories))
//    //    {
//    //        Debug.Log($"[PostBuildProcessor]DIR: {dir}");
//    //    }

//    //    foreach (var file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
//    //    {
//    //        Debug.Log($"[PostBuildProcessor]FILE: {file}");
//    //    }
//    //}

//    [PostProcessBuild(9999)]
//    public static void OnPostProcessBuild(BuildTarget buildTarget, string pathToBuiltProject)
//    {
//        Debug.Log($"[PostBuildProcessor]OnPostProcessBuild({buildTarget}, {pathToBuiltProject})");

//        //iOSじゃなければ処理しない
//        if (buildTarget != BuildTarget.iOS) return;

//        {
//            // Xcodeプロジェクトの編集
//            string projPath = PBXProject.GetPBXProjectPath(pathToBuiltProject);
//            PBXProject project = new PBXProject();
//            project.ReadFromFile(projPath);

//#if UNITY_2019_3_OR_NEWER
//            string targetGuid = project.GetUnityMainTargetGuid();
//            string frameworkTargetGuid = project.GetUnityFrameworkTargetGuid();
//#else
//        string targetGuid = project.TargetGuidByName(PBXProject.GetUnityTargetName());
//        string frameworkTargetGuid = project.TargetGuidByName("UnityFramework");
//#endif

//            // Swift 標準ライブラリの埋め込みを無効に
//            project.SetBuildProperty(frameworkTargetGuid, "ALWAYS_EMBED_SWIFT_STANDARD_LIBRARIES", "NO");
//            Debug.Log("[PostBuildProcessor]Set ALWAYS_EMBED_SWIFT_STANDARD_LIBRARIES = NO");

//            // Enable Modules (C and Objective-C)をYESに設定する
//            project.SetBuildProperty(frameworkTargetGuid, "CLANG_ENABLE_MODULES", "YES");
//            Debug.Log("[PostBuildProcessor]Set CLANG_ENABLE_MODULES = YES");

//            // 他の関連ビルド設定も追加で無効に
//            project.SetBuildProperty(frameworkTargetGuid, "ENABLE_BITCODE", "NO");
//            Debug.Log("[PostBuildProcessor]Set ENABLE_BITCODE = NO");

//            // ここで必要に応じて設定追加例
//            //proj.SetBuildProperty(frameworkTargetGuid, "DEFINES_MODULE", "NO");
//            //proj.SetBuildProperty(frameworkTargetGuid, "CLANG_ENABLE_MODULES", "NO");

//            // 変更を保存
//            project.WriteToFile(projPath);
//        }

//        {
//            string projPath = Path.Combine(pathToBuiltProject, "Unity-iPhone.xcodeproj/project.pbxproj");
//            PBXProject project = new PBXProject();
//            project.ReadFromString(File.ReadAllText(projPath));
//            string target = project.TargetGuidByName("Unity-iPhone");
//            project.AddBuildProperty(target, "OTHER_LDFLAGS", "-ObjC");//　必須！
//            project.AddBuildProperty(target, "OTHER_LDFLAGS", "-l???");
//            /*
//            詳しくは下を参照
//            */
//            List<string> frameworks = new List<string>() {
//            "???.framework"
//        };
//            foreach (var framework in frameworks)
//            {
//                project.AddFrameworkToProject(target, framework, false);
//            }
//            File.WriteAllText(projPath, project.WriteToString());
//        }

//        _CreateEntitlements(pathToBuiltProject, "myapp");
//        _SetCapabilities(pathToBuiltProject);// Push通知を使わないなら多分いらない
//    }

//    private static void _CreateEntitlements(string path, string your_appname)
//    {
//        Debug.Log($"[PostBuildProcessor]_CreateEntitlements({path}, {your_appname})");

//        XmlDocument document = new XmlDocument();
//        XmlDocumentType doctype = document.CreateDocumentType("plist", "-//Apple//DTD PLIST 1.0//EN", "http://www.apple.com/DTDs/PropertyList-1.0.dtd", null);
//        document.AppendChild(doctype);
//        XmlElement plist = document.CreateElement("plist");
//        plist.SetAttribute("version", "1.0");
//        XmlElement dict = document.CreateElement("dict");
//        plist.AppendChild(dict);
//        document.AppendChild(plist);
//        XmlElement e = (XmlElement)document.SelectSingleNode("/plist/dict");
//        XmlElement key = document.CreateElement("key");
//        key.InnerText = "aps-environment";
//        e.AppendChild(key);
//        XmlElement value = document.CreateElement("string");
//        value.InnerText = "development";
//        e.AppendChild(value);
//        string entitlementsPath = Path.Combine(path, "Unity-iPhone/" + your_appname + ".entitlements");
//        //= path + "/Unity-iPhone/" + your_appname + ".entitlements";
//        document.Save(entitlementsPath);

//        string projPath = Path.Combine(path, "Unity-iPhone.xcodeproj/project.pbxproj");
//        PBXProject project = new PBXProject();
//        project.ReadFromString(File.ReadAllText(projPath));
//        string target = project.TargetGuidByName("Unity-iPhone");
//        string guid = project.AddFile(entitlementsPath, entitlementsPath);
//        project.AddBuildProperty(target, "CODE_SIGN_ENTITLEMENTS", Path.Combine(path, "Unity-iPhone/" + your_appname + ".entitlements"));
//        project.AddFileToBuild(target, guid);
//        project.WriteToFile(projPath);
//    }

//    private static void _SetCapabilities(string path)
//    {
//        Debug.Log($"[PostBuildProcessor]_SetCapabilities({path})");

//        string projPath = Path.Combine(path, "Unity-iPhone.xcodeproj/project.pbxproj");
//        PBXProject proj = new PBXProject();
//        proj.ReadFromString(File.ReadAllText(projPath));
//        string target = proj.TargetGuidByName("Unity-iPhone");
//        bool s = proj.AddCapability(target, PBXCapabilityType.PushNotifications);
//        File.WriteAllText(projPath, proj.WriteToString());
//        string[] lines = proj.WriteToString().Split('\n');
//        List<string> newLines = new List<string>();
//        bool editFinish = false;
//        for (int i = 0; i < lines.Length; i++)
//        {
//            string line = lines[i];
//            if (editFinish)
//            {
//                newLines.Add(line);
//            }
//            else if (line.IndexOf("isa = PBXProject;") > -1)
//            {
//                do
//                {
//                    newLines.Add(line);
//                    line = lines[++i];
//                } while (line.IndexOf("TargetAttributes = {") == -1);
//                // この下のやつは多分無くても大丈夫　まあ、一応・・・
//                newLines.Add("TargetAttributes = {");
//                newLines.Add("********* = {");
//                newLines.Add("DevelopmentTeam = ****;");
//                newLines.Add("SystemCapabilities = {");
//                newLines.Add("com.apple.BackgroundModes = {");
//                newLines.Add("enabled = 1;");
//                newLines.Add("};");
//                newLines.Add("com.apple.Push = {");
//                newLines.Add("enabled = 1;");
//                newLines.Add("};");
//                newLines.Add("};");
//                newLines.Add("};");
//                editFinish = true;
//            }
//            else
//            {
//                newLines.Add(line);
//            }
//        }

//        File.WriteAllText(projPath, string.Join("\n", newLines.ToArray()));
//    }
//}
//#endif