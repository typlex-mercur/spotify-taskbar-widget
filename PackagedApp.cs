namespace SpotifyTaskbarWidget;

/// <summary>
/// Deteta se a app corre empacotada (MSIX/Microsoft Store) ou solta (exe/Inno).
/// Na Store: as atualizações são geridas pela própria Store (o auto-updater
/// esconde-se) e o arranque com o Windows usa a StartupTask do pacote em vez
/// do registo (que é virtualizado em MSIX e não teria efeito real).
/// </summary>
internal static class PackagedApp
{
    public static bool IsPackaged { get; } = Detect();

    private static bool Detect()
    {
        try
        {
            return Windows.ApplicationModel.Package.Current != null;
        }
        catch
        {
            return false; // fora de um pacote, Package.Current lança exceção
        }
    }
}
