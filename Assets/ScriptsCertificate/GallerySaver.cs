using System;
using System.IO;
using UnityEngine;

/// <summary>
/// Saves a PNG into the phone's photo gallery. On Android (minSdk 29) this goes through
/// MediaStore into Pictures/AbhyasAR, which needs no storage permission. Elsewhere (the Editor,
/// desktop builds) it writes to the user's Pictures folder so the flow can still be tested.
/// </summary>
public static class GallerySaver
{
    private const string Album = "AbhyasAR";

    /// <summary>Returns true on success; <paramref name="location"/> is where it was saved, or the error.</summary>
    public static bool SavePng(byte[] png, string fileName, out string location)
    {
        location = string.Empty;
        if (png == null || png.Length == 0)
        {
            location = "empty image";
            return false;
        }

        try
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return SaveAndroid(png, fileName, out location);
#else
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), Album);
            if (string.IsNullOrEmpty(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)))
            {
                dir = Path.Combine(Application.persistentDataPath, Album);
            }
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, fileName);
            File.WriteAllBytes(path, png);
            location = path;
            return true;
#endif
        }
        catch (Exception ex)
        {
            Debug.LogError($"GallerySaver: could not save '{fileName}' — {ex}");
            location = ex.Message;
            return false;
        }
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    private static bool SaveAndroid(byte[] png, string fileName, out string location)
    {
        location = string.Empty;
        using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
        using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
        using (var resolver = activity.Call<AndroidJavaObject>("getContentResolver"))
        using (var values = new AndroidJavaObject("android.content.ContentValues"))
        using (var media = new AndroidJavaClass("android.provider.MediaStore$Images$Media"))
        {
            values.Call<AndroidJavaObject>("put", "_display_name", fileName)?.Dispose();
            values.Call<AndroidJavaObject>("put", "mime_type", "image/png")?.Dispose();
            values.Call<AndroidJavaObject>("put", "relative_path", "Pictures/" + Album)?.Dispose();

            using (var collection = media.GetStatic<AndroidJavaObject>("EXTERNAL_CONTENT_URI"))
            using (var uri = resolver.Call<AndroidJavaObject>("insert", collection, values))
            {
                if (uri == null)
                {
                    location = "MediaStore refused the new image";
                    return false;
                }
                using (var stream = resolver.Call<AndroidJavaObject>("openOutputStream", uri))
                {
                    stream.Call("write", (sbyte[])(Array)png);
                    stream.Call("flush");
                    stream.Call("close");
                }
                location = "Pictures/" + Album + "/" + fileName;
                return true;
            }
        }
    }
#endif
}
