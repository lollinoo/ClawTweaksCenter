using ClawTweaksCenter.Library;
using System.IO;
using System.Reflection;
using System.Globalization;

namespace ClawTweaksCenter.Tests;

internal static class SteamDownloadTests
{
    [RegressionTest]
    private static void SteamLogPauseAndResumeOverrideAnUnchangedManifest()
    {
        string root = Path.Combine(Path.GetTempPath(), "claw-steam-log-" + Guid.NewGuid().ToString("N"));
        string apps = Path.Combine(root, "steamapps");
        string logs = Path.Combine(root, "logs");
        Directory.CreateDirectory(apps);
        Directory.CreateDirectory(logs);
        string manifest = Path.Combine(apps, "appmanifest_12345.acf");
        string contentLog = Path.Combine(logs, "content_log.txt");
        string stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        try
        {
            WriteManifest(manifest, 1026); // Steam can leave these flags unchanged on pause.
            File.WriteAllText(contentLog,
                $"[{stamp}] AppID 12345 state changed : Update Required,Update Queued,Update Running,Update Started,\n"
                + $"[{stamp}] AppID 12345 App update changed : Running Update,Downloading,Staging,\n");
            AssertEx.Equal(SteamDownloadStatus.Downloading, ReadManifest(manifest, apps).DownloadStatus);

            File.AppendAllText(contentLog,
                $"[{stamp}] AppID 12345 update canceled : Disabled (Suspended)\n"
                + $"[{stamp}] AppID 12345 App update changed : Running Update,Downloading,Staging,Stopping,\n"
                + $"[{stamp}] AppID 12345 App update changed : None\n"
                + $"[{stamp}] AppID 12345 state changed : Update Required,Update Queued,Update Started, (Suspended)\n"
                + $"[{stamp}] AppID 99999 state changed : Update Required,Update Queued,Update Running,Update Started,\n");
            AssertEx.Equal(SteamDownloadStatus.Paused, ReadManifest(manifest, apps).DownloadStatus);

            File.AppendAllText(contentLog,
                $"[{stamp}] AppID 12345 state changed : Update Required,Update Queued,Update Running,Update Started,\n");
            AssertEx.Equal(SteamDownloadStatus.Downloading, ReadManifest(manifest, apps).DownloadStatus);

            string oldStamp = DateTime.Now.AddHours(-2).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            File.WriteAllText(contentLog,
                $"[{oldStamp}] AppID 12345 state changed : Update Required,Update Queued,Update Started, (Suspended)\n");
            AssertEx.Equal(SteamDownloadStatus.Downloading, ReadManifest(manifest, apps).DownloadStatus);

            WriteManifest(manifest, 4);
            Directory.CreateDirectory(Path.Combine(apps, "common", "Test game"));
            AssertEx.Equal(SteamDownloadStatus.None, ReadManifest(manifest, apps).DownloadStatus);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [RegressionTest]
    private static void PausedStateSurvivesManifestRewriteWithoutAResumeEvent()
    {
        string root = Path.Combine(Path.GetTempPath(), "claw-steam-rewrite-" + Guid.NewGuid().ToString("N"));
        string apps = Path.Combine(root, "steamapps");
        string logs = Path.Combine(root, "logs");
        Directory.CreateDirectory(apps);
        Directory.CreateDirectory(logs);
        string manifest = Path.Combine(apps, "appmanifest_12345.acf");
        try
        {
            WriteManifest(manifest, 1026);
            File.SetCreationTime(manifest, DateTime.Now.AddHours(-1));
            string stamp = DateTime.Now.AddMinutes(-10).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            File.WriteAllText(Path.Combine(logs, "content_log.txt"),
                $"[{stamp}] AppID 12345 state changed : Update Required,Update Queued,Update Started, (Suspended)\n");
            File.SetLastWriteTime(manifest, DateTime.Now);

            AssertEx.Equal(SteamDownloadStatus.Paused, ReadManifest(manifest, apps).DownloadStatus);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [RegressionTest]
    private static void PausedManifestStopsShowingAnActiveDownload()
    {
        string root = Path.Combine(Path.GetTempPath(), "claw-steam-download-" + Guid.NewGuid().ToString("N"));
        string apps = Path.Combine(root, "steamapps");
        Directory.CreateDirectory(apps);
        string manifest = Path.Combine(apps, "appmanifest_12345.acf");
        try
        {
            WriteManifest(manifest, 1026); // Required + Started; Steam may omit Running mid-download.
            var running = ReadManifest(manifest, apps);
            AssertEx.True(running.Downloading);

            WriteManifest(manifest, 1034); // Required + Queued + Started; no Running.
            var paused = ReadManifest(manifest, apps);
            AssertEx.False(paused.Downloading, "A suspended Steam transfer is not actively downloading.");
            AssertEx.False(paused.Installed);
            AssertEx.Equal(SteamDownloadStatus.Paused, paused.DownloadStatus);

            WriteManifest(manifest, 2); // Required but not started yet.
            AssertEx.Equal(SteamDownloadStatus.Queued, ReadManifest(manifest, apps).DownloadStatus);

            WriteManifest(manifest, 1538); // Required + Paused + Started.
            AssertEx.Equal(SteamDownloadStatus.Paused, ReadManifest(manifest, apps).DownloadStatus);

            WriteManifest(manifest, 1026);
            AssertEx.Equal(SteamDownloadStatus.Downloading, ReadManifest(manifest, apps).DownloadStatus);

            WriteManifest(manifest, 1290); // Required + Queued + Running + Started.
            AssertEx.Equal(SteamDownloadStatus.Downloading, ReadManifest(manifest, apps).DownloadStatus);

            WriteManifest(manifest, 4);
            Directory.CreateDirectory(Path.Combine(apps, "common", "Test game"));
            AssertEx.Equal(SteamDownloadStatus.None, ReadManifest(manifest, apps).DownloadStatus);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [RegressionTest]
    private static void IncompleteSteamGameAppearsOnSteamShelfInsteadOfRecent()
    {
        var library = new GameLibrary();
        var download = new GameEntry
        {
            Id = "12345", Title = "Downloading game", Store = GameStore.Steam,
            Installed = false, DownloadStatus = SteamDownloadStatus.Downloading,
        };
        var paused = new GameEntry
        {
            Id = "23456", Title = "Paused game", Store = GameStore.Steam,
            Installed = false, DownloadStatus = SteamDownloadStatus.Paused,
        };
        var ownedOnly = new GameEntry
        {
            Id = "34567", Title = "Not installed", Store = GameStore.Steam,
            Installed = false,
        };
        typeof(GameLibrary).GetProperty(nameof(GameLibrary.Games))!
            .SetValue(library, new[] { download, paused, ownedOnly });

        AssertEx.True(library.ForGroup(LibraryGroup.Steam).Contains(download));
        AssertEx.True(library.ForGroup(LibraryGroup.Steam).Contains(paused));
        AssertEx.False(library.ForGroup(LibraryGroup.Steam).Contains(ownedOnly));
        AssertEx.False(library.ForGroup(LibraryGroup.Recent).Contains(download));
        AssertEx.False(library.ForGroup(LibraryGroup.Recent).Contains(paused));
    }

    private static GameEntry ReadManifest(string path, string apps)
        => SteamSource.ReadManifest(path, apps,
            SteamSource.ReadContentLogStates(Directory.GetParent(apps)!.FullName));

    private static void WriteManifest(string path, int flags)
        => File.WriteAllText(path, "\"AppState\"\n{\n"
            + "\"appid\" \"12345\"\n\"name\" \"Test game\"\n"
            + "\"installdir\" \"Test game\"\n\"StateFlags\" \"" + flags + "\"\n}");
}
