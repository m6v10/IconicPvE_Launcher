namespace IconicLauncher.Core.Utils;

public static class AtomicFile
{
    public static void WriteAllTextAtomic(string path, string text)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, text);
        if (File.Exists(path))
        {
            try
            {
                File.Replace(tmp, path, null);
            }
            catch (IOException)
            {
                File.Move(tmp, path, true);
            }
        }
        else
        {
            File.Move(tmp, path);
        }
    }
}
