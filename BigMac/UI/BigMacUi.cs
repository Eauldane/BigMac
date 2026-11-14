using Dalamud.Interface;
using ElezenTools.UI;

namespace BigMac.UI;

internal static class BigMacUi
{
    public static bool IconButton(FontAwesomeIcon icon, string label, float? width = null)
        => ElezenImgui.ShowIconButton(icon, " " + label, width);

    public static float IconButtonWidth(FontAwesomeIcon icon, string label)
        => ElezenImgui.GetIconButtonTextSize(icon, " " + label);
}
