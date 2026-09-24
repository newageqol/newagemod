using BepInEx;

namespace NewAge2D;

public static class CacheReset
{
    public static string Run()
    {
        long bytes = 0;
        int files = 0;
        try
        {
            DollWorker.Clear();
            FrameCache.Clear();
            Plugin.Store?.Forget();
            Warmup.Forget();
            ThingImages.Forget();
            Field.Forget();
            string root = Path.Combine(Paths.CachePath, "NewAge2D");
            foreach (string folder in new[] { "swf", "dolls" })
            {
                string path = Path.Combine(root, folder);
                if (!Directory.Exists(path)) continue;
                foreach (var file in new DirectoryInfo(path).GetFiles("*", SearchOption.AllDirectories))
                {
                    try
                    {
                        long size = file.Length;
                        file.Delete();
                        bytes += size;
                        files++;
                    }
                    catch { }
                }
            }
            var things = new FileInfo(Path.Combine(root, "things.txt"));
            if (things.Exists)
            {
                try
                {
                    long size = things.Length;
                    things.Delete();
                    bytes += size;
                    files++;
                }
                catch { }
            }
            string text = $"Кэш вещей сброшен: удалено файлов {files}, {bytes / 1048576.0:0.0} МБ. Тела персонажей и эффекты заклинаний лежат в папке мода и не сбрасываются. Для чистого первого запуска перезапусти клиент";
            Plugin.Log.LogInfo(text);
            if (Trace.On) Trace.Write(text);
            return text;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError("[кэш] сброс: " + ex);
            return "Кэш вещей сброшен не полностью: " + ex.Message;
        }
    }
}
