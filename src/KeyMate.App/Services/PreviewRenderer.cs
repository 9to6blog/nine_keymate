using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using KeyMate.Core;

namespace KeyMate.Services;

internal static class PreviewRenderer
{
    public static async Task Render(MainWindow window, string directory)
    {
        await Task.Delay(200);
        App.Current.Engine.Paused = false; window.UpdateStatus();
        Save(window, Path.Combine(directory, "keymate-main.png"));
        window.Navigate("settings"); ThemeService.Apply("Dark"); await Task.Delay(100);
        Save(window, Path.Combine(directory, "keymate-settings.png"));
        ThemeService.Apply("Light");
        var editor = new EditorWindow(Rules.Examples()[0], Rules.Examples()) { Owner = window };
        editor.Show(); await Task.Delay(100); Save(editor, Path.Combine(directory, "keymate-editor.png")); editor.Close();
    }
    private static void Save(Window window, string path)
    {
        window.UpdateLayout();
        var visual = (FrameworkElement)window.Content;
        var bitmap = new RenderTargetBitmap((int)visual.ActualWidth, (int)visual.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path); encoder.Save(file);
    }
}
