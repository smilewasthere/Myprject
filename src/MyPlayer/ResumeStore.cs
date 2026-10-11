using System.IO;
using System.Text.Json;

namespace MyPlayer;

/// <summary>
/// Remembers where you left off in each local file, like VLC's "continue
/// playback?". Persisted as plain JSON in the config dir — separate from
/// mpv's own files, so it stays predictable and easy to inspect/edit.
/// </summary>
public sealed class ResumeStore
{
    private sealed class Entry
    {
        public double Position { get; set; }
        public double Duration { get; set; }
    }

    private readonly System.Collections.Generic.Dictionary<string, Entry> _data = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _path;

    // Don't resume if within this many seconds of the start (nothing to resume)
    // or this close to the end (already watched).
    private const double MinResume = 5;
    private const double EndWindow = 15;

    public ResumeStore(string path)
    {
        _path = path;
        Load();
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_path))
                return;
            var json = File.ReadAllText(_path);
            var parsed = JsonSerializer.Deserialize<System.Collections.Generic.Dictionary<string, Entry>>(json);
            if (parsed is not null)
                foreach (var kv in parsed)
                    _data[kv.Key] = kv.Value;
        }
        catch
        {
            // Corrupt/absent resume file is never fatal — start fresh.
        }
    }

    public void Save()
    {
        try
        {
            var dir = System.IO.Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir))
                System.IO.Directory.CreateDirectory(dir);
            File.WriteAllText(_path, JsonSerializer.Serialize(_data));
        }
        catch
        {
            // Ignore transient write failures — resume is a nicety, not critical.
        }
    }

    /// <summary>
    /// Records the current position. Positions near the very start or the very
    /// end are treated as "nothing to resume" and the entry is removed.
    /// </summary>
    public void Record(string path, double position, double duration)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Contains("://"))
            return;
        if (position < MinResume || (duration > 0 && position > duration - EndWindow))
        {
            if (_data.Remove(path))
                Save();
            return;
        }
        _data[path] = new Entry { Position = position, Duration = duration };
        Save();
    }

    /// <summary>
    /// Returns a position to resume to, or null if there's nothing useful.
    /// </summary>
    public double? Get(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Contains("://"))
            return null;
        if (!_data.TryGetValue(path, out var e))
            return null;
        if (e.Position < MinResume || (e.Duration > 0 && e.Position > e.Duration - EndWindow))
            return null;
        return e.Position;
    }
}
