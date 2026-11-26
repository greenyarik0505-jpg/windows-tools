namespace WindowsTools.Core.Modules;

/// <summary>
/// Describes a WindowsTools module for display, filtering, and lifecycle management.
/// </summary>
/// <param name="Name">Human-readable module name.</param>
/// <param name="Description">Short summary of what the module provides.</param>
/// <param name="IconGlyph">Optional Segoe Fluent Icons glyph code.</param>
/// <param name="IsExperimental">Whether the module is experimental and hidden or badged in UI.</param>
/// <param name="RequiresAdmin">Whether full functionality requires elevated privileges.</param>
/// <param name="EnabledByDefault">Whether the module is enabled when no saved user preference exists.</param>
public sealed record ModuleMetadata(
    string Name,
    string Description,
    string? IconGlyph = null,
    bool IsExperimental = false,
    bool RequiresAdmin = false,
    bool EnabledByDefault = false);
