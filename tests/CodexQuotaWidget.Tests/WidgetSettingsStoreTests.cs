using CodexQuotaWidget.Services;

namespace CodexQuotaWidget.Tests;

public sealed class WidgetSettingsStoreTests
{
    [Fact]
    public void SaveAndLoad_RoundTripsValidPosition()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"codex-quota-settings-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "settings.json");

        try
        {
            var store = new WidgetSettingsStore(path);
            var expected = new WidgetPosition(120, 240);

            store.Save(expected);
            var actual = store.Load(
                new WidgetBounds(0, 0, 1920, 1080),
                widgetWidth: 320,
                widgetHeight: 240);

            Assert.Equal(expected, actual);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void Load_UsesTopRightDefaultWhenSettingsAreMissing()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"codex-quota-missing-{Guid.NewGuid():N}",
            "settings.json");
        var store = new WidgetSettingsStore(path);

        var position = store.Load(
            new WidgetBounds(0, 0, 1920, 1080),
            widgetWidth: 320,
            widgetHeight: 240);

        Assert.Equal(new WidgetPosition(1576, 24), position);
    }

    [Fact]
    public void Load_UsesDefaultWhenSettingsJsonIsMalformed()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"codex-quota-malformed-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "settings.json");

        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, "{not-json");

            var position = new WidgetSettingsStore(path).Load(
                new WidgetBounds(0, 0, 1920, 1080),
                widgetWidth: 320,
                widgetHeight: 240);

            Assert.Equal(new WidgetPosition(1576, 24), position);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void Load_ClampsSavedPositionIntoVirtualScreenBounds()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"codex-quota-clamp-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "settings.json");

        try
        {
            var store = new WidgetSettingsStore(path);
            store.Save(new WidgetPosition(-500, 4000));

            var position = store.Load(
                new WidgetBounds(0, 0, 1920, 1080),
                widgetWidth: 320,
                widgetHeight: 240);

            Assert.Equal(new WidgetPosition(0, 840), position);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
