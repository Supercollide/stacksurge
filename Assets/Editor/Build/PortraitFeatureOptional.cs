using System.IO;
using System.Xml;
using UnityEditor.Android;
using UnityEngine;

/// <summary>
/// The game is locked to portrait (Player Settings, Default Orientation). Google Play reads that lock as a
/// requirement for android.hardware.screen.portrait and hides the game from devices whose screen doesn't turn
/// (the Play Console names desktops such as Chromebooks, Google Play Games on PC, Android XR, and some phones and
/// tablets). Declaring the feature with required="false" lifts that filter. The game itself stays in portrait
/// everywhere; on those devices Android shows it in a portrait window or with bars at the sides.
/// </summary>
class PortraitFeatureOptional : IPostGenerateGradleAndroidProject
{
    const string AndroidNs = "http://schemas.android.com/apk/res/android";
    const string Feature = "android.hardware.screen.portrait";

    public int callbackOrder => 0;

    public void OnPostGenerateGradleAndroidProject(string unityLibraryPath)
    {
        string manifestPath = Path.Combine(unityLibraryPath, "src", "main", "AndroidManifest.xml");
        if (!File.Exists(manifestPath))
        {
            Debug.LogWarning($"[PortraitFeatureOptional] No manifest at {manifestPath}; Play will treat portrait as required.");
            return;
        }

        var doc = new XmlDocument();
        doc.Load(manifestPath);
        MarkOptional(doc);
        doc.Save(manifestPath);
    }

    /// <summary>Sets required="false" on the feature, adding the declaration if the manifest has none.</summary>
    public static void MarkOptional(XmlDocument doc)
    {
        var manifest = doc.DocumentElement;
        foreach (XmlNode node in manifest.ChildNodes)
        {
            if (node is XmlElement e && e.Name == "uses-feature" && e.GetAttribute("name", AndroidNs) == Feature)
            {
                e.SetAttribute("required", AndroidNs, "false");
                return;
            }
        }

        var feature = doc.CreateElement("uses-feature");
        feature.Attributes.Append(AndroidAttribute(doc, "name", Feature));
        feature.Attributes.Append(AndroidAttribute(doc, "required", "false"));

        // Next to Unity's own uses-feature lines, before <application>.
        var application = manifest["application"];
        if (application != null) manifest.InsertBefore(feature, application);
        else manifest.AppendChild(feature);
    }

    static XmlAttribute AndroidAttribute(XmlDocument doc, string name, string value)
    {
        var attribute = doc.CreateAttribute("android", name, AndroidNs);
        attribute.Value = value;
        return attribute;
    }
}
