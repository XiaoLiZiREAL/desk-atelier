using System;
using System.IO;
using System.Text;
using Windows.Media;
using Windows.Media.Core;
using Windows.Media.Playback;

// Publish a silent track through the real Windows media service for end-to-end detection tests.
internal sealed class NativeTestSong : IDisposable
{
    public readonly MediaPlayer Player = new MediaPlayer();
    public readonly SystemMediaTransportControls Controls;

    public NativeTestSong(string folder, string title, string artist)
    {
        Controls = Player.SystemMediaTransportControls;
        Player.CommandManager.IsEnabled = false;
        Player.Volume = 0;
        Player.Source = MediaSource.CreateFromUri(new Uri(SilentWav(folder)));
        Player.Play();
        Controls.IsEnabled = Controls.IsPauseEnabled = Controls.IsPlayEnabled = true;
        Track(title,artist);
        Controls.PlaybackStatus = MediaPlaybackStatus.Playing;
        Position(0);
    }

    public void Track(string title, string artist)
    {
        Controls.DisplayUpdater.Type = MediaPlaybackType.Music;
        Controls.DisplayUpdater.MusicProperties.Title = title;
        Controls.DisplayUpdater.MusicProperties.Artist = artist;
        Controls.DisplayUpdater.Update();
    }

    public void Position(double seconds)
    {
        Controls.UpdateTimelineProperties(new SystemMediaTransportControlsTimelineProperties {
            StartTime = TimeSpan.Zero, MinSeekTime = TimeSpan.Zero,
            MaxSeekTime = TimeSpan.FromMinutes(2), EndTime = TimeSpan.FromMinutes(2),
            Position = TimeSpan.FromSeconds(seconds)
        });
    }

    static string SilentWav(string folder)
    {
        string path = Path.Combine(folder, "silent-test.wav");
        if (File.Exists(path)) return path;
        int length = 120 * 8000 * 2;
        using (var writer = new BinaryWriter(File.Create(path)))
        {
            writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + length);
            writer.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16);
            writer.Write((short)1); writer.Write((short)1); writer.Write(8000);
            writer.Write(16000); writer.Write((short)2); writer.Write((short)16);
            writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(length); writer.Write(new byte[length]);
        }
        return path;
    }

    public void Dispose()
    {
        try { Controls.PlaybackStatus = MediaPlaybackStatus.Closed; Controls.IsEnabled = false; Player.Pause(); }
        finally { Player.Dispose(); }
    }
}
