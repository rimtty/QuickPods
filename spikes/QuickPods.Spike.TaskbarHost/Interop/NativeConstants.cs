namespace QuickPods.Spike.TaskbarHost.Interop;

internal static class NativeConstants
{
    internal const int ErrorClassAlreadyExists = 1410;

    internal const int GwlpUserData = -21;
    internal const int GwlStyle = -16;
    internal const int GwlExtendedStyle = -20;
    internal const uint GetAncestorParent = 1;
    internal const uint GetAncestorRoot = 2;
    internal const uint GetWindowOwner = 4;
    internal const uint MonitorDefaultToNearest = 2;
    internal const int MonitorDpiTypeEffective = 0;
    internal const uint DwmWindowAttributeCloaked = 14;

    internal const long WindowStyleChild = 0x40000000L;
    internal const long WindowStyleClipSiblings = 0x04000000L;
    internal const long WindowStylePopup = 0x80000000L;
    internal const long WindowStyleVisible = 0x10000000L;

    internal const long WindowExtendedStyleToolWindow = 0x00000080L;
    internal const long WindowExtendedStyleTopmost = 0x00000008L;
    internal const long WindowExtendedStyleLayered = 0x00080000L;
    internal const long WindowExtendedStyleNoActivate = 0x08000000L;

    internal const uint WmDestroy = 0x0002;
    internal const uint WmPaint = 0x000F;
    internal const uint WmEraseBackground = 0x0014;
    internal const uint WmSettingChange = 0x001A;
    internal const uint WmDisplayChange = 0x007E;
    internal const uint WmQuit = 0x0012;
    internal const uint WmNcCreate = 0x0081;
    internal const uint WmNcDestroy = 0x0082;
    internal const uint WmMouseActivate = 0x0021;
    internal const uint WmThemeChanged = 0x031A;
    internal const uint WmMouseMove = 0x0200;
    internal const uint WmLeftButtonDown = 0x0201;
    internal const uint WmLeftButtonUp = 0x0202;
    internal const uint WmMouseWheel = 0x020A;
    internal const uint WmCaptureChanged = 0x0215;
    internal const uint WmDpiChanged = 0x02E0;
    internal const uint WmDpiChangedBeforeParent = 0x02E2;
    internal const uint WmDpiChangedAfterParent = 0x02E3;

    internal const nint MouseActivateNoActivate = 3;

    internal const int ShowWindowHide = 0;
    internal const int ShowWindowNoActivate = 8;

    internal const uint SetWindowPositionNoSize = 0x0001;
    internal const uint SetWindowPositionNoMove = 0x0002;
    internal const uint SetWindowPositionNoZOrder = 0x0004;
    internal const uint SetWindowPositionNoActivate = 0x0010;
    internal const uint SetWindowPositionFrameChanged = 0x0020;
    internal const uint SetWindowPositionNoOwnerZOrder = 0x0200;

    internal const uint LayeredWindowAttributeColorKey = 0x00000001;
    internal const uint TransparentColorKey = 0x00FF00FF;
    internal const byte FullOpacity = 255;

    internal const uint PeekMessageRemove = 0x0001;

    internal const int PenStyleSolid = 0;
    internal const uint RasterOperationSourceCopy = 0x00CC0020;
    internal const int GuiResourceGdiObjects = 0;
    internal const int GuiResourceUserObjects = 1;
}
