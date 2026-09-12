using System.ComponentModel;
using System.Diagnostics;

namespace ConflictStudio.App;

public static class SourceFileActions
{
    public static void OpenEditor(SourceFileLocation file)
    {
        string path = SourceFileLocations.ExistingPath(file);
        try
        {
            using Process? editor = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, Verb = "edit" });
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode is 1155 or 31)
        {
            ProcessStartInfo notepad = new("notepad.exe") { UseShellExecute = false };
            notepad.ArgumentList.Add(path);
            using Process? editor = Process.Start(notepad);
        }
    }

    public static void ShowInFolder(SourceFileLocation file)
    {
        string path = SourceFileLocations.ExistingPath(file);
        using Process? explorer = Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
    }
}
