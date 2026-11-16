namespace OpenUI.Lang.Blazor.Mud;

/// <summary>Which palette a person wants.</summary>
public enum ThemeMode
{
    /// <summary>Whichever one their system asks for, and it follows when that changes.</summary>
    System,

    /// <summary>Always the light palette.</summary>
    Light,

    /// <summary>Always the dark palette.</summary>
    Dark,
}
