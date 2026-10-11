using Godot;

namespace Rts.Game;

/// <summary>The controls the shell screens share (M6-V1): a backdrop, a centred column, a heading and a button. Look only; the text comes from the caller (ui.json).</summary>
public static class ShellUi
{
    /// <summary>Width of every shell button, in pixels.</summary>
    public const float ButtonWidth = 260f;

    /// <summary>A full-screen colour that takes every click, so nothing under the screen gets one.</summary>
    public static ColorRect Backdrop(Color color)
    {
        var rect = new ColorRect { Name = "Backdrop", Color = color, MouseFilter = Control.MouseFilterEnum.Stop };
        rect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        return rect;
    }

    /// <summary>A full-screen centre with a vertical column in it; returns the column.</summary>
    public static VBoxContainer CentredColumn(Node parent, int separation = 14)
    {
        var centre = new CenterContainer { Name = "Centre", MouseFilter = Control.MouseFilterEnum.Ignore };
        centre.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        parent.AddChild(centre);
        var column = new VBoxContainer { Name = "Column" };
        column.AddThemeConstantOverride("separation", separation);
        centre.AddChild(column);
        return column;
    }

    /// <summary>A large centred heading.</summary>
    public static Label Heading(string text, int size = 40)
    {
        var label = new Label { Name = "Title", Text = text, HorizontalAlignment = HorizontalAlignment.Center };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", new Color(1f, 0.93f, 0.75f));
        label.AddThemeColorOverride("font_outline_color", Colors.Black);
        label.AddThemeConstantOverride("outline_size", 4);
        return label;
    }

    /// <summary>A plain label at the menu font size.</summary>
    public static Label Text(string text, int size = 20)
    {
        var label = new Label { Text = text, VerticalAlignment = VerticalAlignment.Center };
        label.AddThemeFontSizeOverride("font_size", size);
        return label;
    }

    /// <summary>A shell button.</summary>
    public static Button Button(string name, string text, float width = ButtonWidth)
    {
        var button = new Button { Name = name, Text = text, CustomMinimumSize = new Vector2(width, 44f) };
        button.AddThemeFontSizeOverride("font_size", 22);
        return button;
    }
}
