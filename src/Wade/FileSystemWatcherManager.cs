using Wade.Terminal;

namespace Wade;

internal sealed class FileSystemWatcherManager : IDisposable
{
    private const int DebounceMs = 300;

    private readonly InputPipeline _pipeline;
    private Timer? _debounceTimer;
    private bool _disposed;
    private string? _watchedPath;
    private FileSystemWatcher? _watcher;

#pragma warning disable CSLINT221 // Consider using a primary constructor
    public FileSystemWatcherManager(InputPipeline pipeline)
#pragma warning restore CSLINT221
    {
        _pipeline = pipeline;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();
    }

    public void Watch(string directoryPath)
    {
        if (_disposed)
        {
            return;
        }

        // Already watching this directory
        if (_watchedPath is not null &&
            string.Equals(_watchedPath, directoryPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Stop();

        try
        {
            _watcher = new FileSystemWatcher(directoryPath)
            {
                IncludeSubdirectories = false,
                NotifyFilter = NotifyFilters.FileName
                               | NotifyFilters.DirectoryName
                               | NotifyFilters.LastWrite
                               | NotifyFilters.Size,
            };

            _watcher.Changed += OnFileSystemEvent;
            _watcher.Created += OnFileSystemEvent;
            _watcher.Deleted += OnFileSystemEvent;
            _watcher.Renamed += OnFileSystemEvent;
            _watcher.Error += OnError;
            _watcher.EnableRaisingEvents = true;
            _watchedPath = directoryPath;
        }
        catch (Exception)
        {
            // Directory may not exist, be inaccessible, or watcher may fail to start.
            // Silently degrade — manual refresh still works.
            _watcher?.Dispose();
            _watcher = null;
            _watchedPath = null;
        }
    }

    public void Stop()
    {
        _debounceTimer?.Dispose();
        _debounceTimer = null;

        if (_watcher is not null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
            _watcher = null;
        }

        _watchedPath = null;
    }

    private void OnFileSystemEvent(object sender, FileSystemEventArgs e) => ScheduleDebouncedEvent(fullRefresh: false);

    private void OnError(object sender, ErrorEventArgs e)
    {
        // Buffer overflow or other watcher error — request full refresh
        ScheduleDebouncedEvent(fullRefresh: true);
    }

    private void ScheduleDebouncedEvent(bool fullRefresh)
    {
        if (_disposed || _watchedPath is null)
        {
            return;
        }

        string path = _watchedPath;

        // Reset or start the debounce timer
        _debounceTimer?.Dispose();
        _debounceTimer = new Timer(
            _ => FireEvent(path, fullRefresh),
            state: null,
            dueTime: DebounceMs,
            period: Timeout.Infinite);
    }

    private void FireEvent(string directoryPath, bool fullRefresh)
    {
        try
        {
            _pipeline.Inject(new FileSystemChangedEvent(directoryPath, fullRefresh));
        }
        catch (InvalidOperationException)
        {
            // Pipeline disposed / completed adding
        }
    }
}
