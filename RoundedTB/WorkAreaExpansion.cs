namespace RoundedTB;

internal static class WorkAreaExpansion
{
    internal static bool Enabled(Types.Settings settings) => settings.ExpandMaximizedWorkArea;

    internal static bool RestoreOnMaximize(Types.Settings settings) => settings.FillOnMaximise && !settings.ExpandMaximizedWorkArea;

}
