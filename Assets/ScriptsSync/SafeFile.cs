using System.IO;

/// <summary>Atomic file writes, so a crash or battery death mid-write never leaves a half-written JSON file.</summary>
public static class SafeFile
{
    public static void WriteAllText(string path, string contents)
    {
        string temp = path + ".tmp";
        File.WriteAllText(temp, contents);
        if (File.Exists(path))
        {
            File.Replace(temp, path, null);
        }
        else
        {
            File.Move(temp, path);
        }
    }
}
