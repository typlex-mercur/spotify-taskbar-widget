using System.Diagnostics;

namespace SpotifyTaskbarWidget;

internal static class SpotifyActions
{
    /// <summary>
    /// Adiciona/remove a faixa atual dos favoritos enviando o atalho oficial do
    /// Spotify (Alt+Shift+B) à janela dele. A API de media do Windows não expõe
    /// "guardar nos favoritos", por isso é preciso dar foco brevemente ao Spotify.
    /// Correr numa thread de background (usa Sleep).
    /// </summary>
    public static void LikeCurrentTrack()
    {
        IntPtr spotify = Interop.GetSpotifyMainWindow();
        if (spotify == IntPtr.Zero) return;
        IntPtr previous = Interop.GetForegroundWindow();

        bool wasMinimized = Interop.IsIconic(spotify);
        int originalExStyle = 0;
        try
        {
            if (wasMinimized)
            {
                originalExStyle = Interop.GetWindowLong(spotify, Interop.GWL_EXSTYLE);
                Interop.SetWindowLong(spotify, Interop.GWL_EXSTYLE, originalExStyle | Interop.WS_EX_LAYERED);
                Interop.SetLayeredWindowAttributes(spotify, 0, 0, Interop.LWA_ALPHA);
                Interop.ShowWindow(spotify, Interop.SW_RESTORE);
                Thread.Sleep(200);
            }

            // "Toque" no Alt liberta a restrição do SetForegroundWindow
            Interop.keybd_event(Interop.VK_MENU, 0, 0, UIntPtr.Zero);
            Interop.keybd_event(Interop.VK_MENU, 0, Interop.KEYEVENTF_KEYUP, UIntPtr.Zero);
            Interop.SetForegroundWindow(spotify);
            Thread.Sleep(150);

            Interop.keybd_event(Interop.VK_MENU, 0, 0, UIntPtr.Zero);
            Interop.keybd_event(Interop.VK_SHIFT, 0, 0, UIntPtr.Zero);
            Interop.keybd_event(Interop.VK_B, 0, 0, UIntPtr.Zero);
            Interop.keybd_event(Interop.VK_B, 0, Interop.KEYEVENTF_KEYUP, UIntPtr.Zero);
            Interop.keybd_event(Interop.VK_SHIFT, 0, Interop.KEYEVENTF_KEYUP, UIntPtr.Zero);
            Interop.keybd_event(Interop.VK_MENU, 0, Interop.KEYEVENTF_KEYUP, UIntPtr.Zero);
            Thread.Sleep(200);
        }
        finally
        {
            if (wasMinimized)
            {
                Interop.ShowWindow(spotify, Interop.SW_MINIMIZE);
                Interop.SetWindowLong(spotify, Interop.GWL_EXSTYLE, originalExStyle);
            }
            if (previous != IntPtr.Zero && Interop.GetForegroundWindow() != previous)
                Interop.SetForegroundWindow(previous);
        }
    }

    public static void OpenSpotifyWindow()
    {
        IntPtr spotify = Interop.GetSpotifyMainWindow();
        if (spotify != IntPtr.Zero)
        {
            IntPtr fg = Interop.GetForegroundWindow();
            bool isIconic = Interop.IsIconic(spotify);

            // If Spotify is currently focused and visible on screen -> minimize it
            if (fg == spotify && !isIconic)
            {
                Interop.ShowWindow(spotify, Interop.SW_MINIMIZE);
            }
            else
            {
                // Otherwise restore and bring it to front
                Interop.ShowWindow(spotify, Interop.SW_RESTORE);
                Interop.SetForegroundWindow(spotify);
            }
        }
        else
        {
            try
            {
                Process.Start(new ProcessStartInfo("spotify:") { UseShellExecute = true });
            }
            catch { }
        }
    }
}
