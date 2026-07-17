using System.IO;
using System.Text.Json;

namespace CodexQuotaWidget.Services;

public readonly record struct WidgetPosition(double Left, double Top);

public readonly record struct WidgetBounds(double Left, double Top, double Width, double Height);

public sealed class WidgetSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _settingsPath;

    public WidgetSettingsStore(string settingsPath)
    {
        _settingsPath = settingsPath;
    }

    public static WidgetSettingsStore CreateDefault()
    {
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CodexQuotaWidget",
            "settings.json");
        return new WidgetSettingsStore(path);
    }

    public WidgetPosition Load(WidgetBounds bounds, double widgetWidth, double widgetHeight)
        => Load(bounds, bounds, widgetWidth, widgetHeight);

    public WidgetPosition Load(
        WidgetBounds visibleBounds,
        WidgetBounds defaultBounds,
        double widgetWidth,
        double widgetHeight)
    {
        var fallback = Clamp(
            GetDefaultPosition(defaultBounds, widgetWidth, widgetHeight),
            visibleBounds,
            widgetWidth,
            widgetHeight);
        try
        {
            if (!File.Exists(_settingsPath))
            {
                return fallback;
            }

            using var stream = new FileStream(
                _settingsPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            var saved = JsonSerializer.Deserialize<WidgetPosition>(stream, JsonOptions);
            if (!double.IsFinite(saved.Left) || !double.IsFinite(saved.Top))
            {
                return fallback;
            }

            return Clamp(saved, visibleBounds, widgetWidth, widgetHeight);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return fallback;
        }
    }

    public void Save(WidgetPosition position)
    {
        if (!double.IsFinite(position.Left) || !double.IsFinite(position.Top))
        {
            return;
        }

        string? temporaryPath = null;
        try
        {
            var directory = Path.GetDirectoryName(_settingsPath);
            if (string.IsNullOrWhiteSpace(directory))
            {
                return;
            }

            Directory.CreateDirectory(directory);
            temporaryPath = Path.Combine(
                directory,
                $".{Path.GetFileName(_settingsPath)}.{Guid.NewGuid():N}.tmp");
            using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.WriteThrough))
            {
                JsonSerializer.Serialize(stream, position, JsonOptions);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(_settingsPath))
            {
                try
                {
                    File.Replace(temporaryPath, _settingsPath, destinationBackupFileName: null);
                }
                catch (IOException)
                {
                    File.Move(temporaryPath, _settingsPath, overwrite: true);
                }
            }
            else
            {
                File.Move(temporaryPath, _settingsPath);
            }

            temporaryPath = null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Position persistence is best-effort and must not stop the widget.
        }
        finally
        {
            if (temporaryPath is not null)
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    // Best-effort cleanup of a file created by this store.
                }
            }
        }
    }

    private static WidgetPosition GetDefaultPosition(
        WidgetBounds bounds,
        double widgetWidth,
        double widgetHeight) =>
        Clamp(
            new WidgetPosition(
                bounds.Left + bounds.Width - widgetWidth - 24,
                bounds.Top + 24),
            bounds,
            widgetWidth,
            widgetHeight);

    private static WidgetPosition Clamp(
        WidgetPosition position,
        WidgetBounds bounds,
        double widgetWidth,
        double widgetHeight)
    {
        var maximumLeft = bounds.Left + Math.Max(0, bounds.Width - Math.Max(0, widgetWidth));
        var maximumTop = bounds.Top + Math.Max(0, bounds.Height - Math.Max(0, widgetHeight));
        return new WidgetPosition(
            Math.Clamp(position.Left, bounds.Left, maximumLeft),
            Math.Clamp(position.Top, bounds.Top, maximumTop));
    }
}
