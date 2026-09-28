using System.Runtime.InteropServices;

namespace v2rayN.Desktop.Common;

/// <summary>
/// Minimal Objective-C runtime bridge. Only the subset of AppKit needed to own an
/// NSStatusItem is exposed; Avalonia's own tray support cannot distinguish left/right
/// clicks on macOS, so the menu-bar item is driven natively.
/// </summary>
[SupportedOSPlatform("macos")]
internal static class ObjC
{
    private const string LibObjC = "/usr/lib/libobjc.dylib";

    public static nint GetClass(string name) => objc_getClass(name);

    public static nint Sel(string name) => sel_registerName(name);

    public static nint Retain(nint obj) => objc_retain(obj);

    public static nint Alloc(nint cls) => Send(cls, "alloc");

    public static nint Init(nint obj) => Send(obj, "init");

    public static nint New(string className) => Init(Alloc(GetClass(className)));

    public static nint NSString(string value)
    {
        var ptr = Marshal.StringToCoTaskMemUTF8(value);
        try
        {
            return Send1(GetClass("NSString"), "stringWithUTF8String:", ptr);
        }
        finally
        {
            Marshal.FreeCoTaskMem(ptr);
        }
    }

    public static nint NSData(byte[] bytes)
    {
        var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try
        {
            return Send2(GetClass("NSData"), "dataWithBytes:length:", handle.AddrOfPinnedObject(), bytes.Length);
        }
        finally
        {
            handle.Free();
        }
    }

    /// <summary>Builds an NSImage from PNG bytes, sized in points and optionally marked as a template image.</summary>
    public static nint NewImage(byte[] png, double widthPt, double heightPt, bool template)
    {
        var data = NSData(png);
        var image = Send1(Init(Alloc(GetClass("NSImage"))), "initWithData:", data);
        if (template)
        {
            SendBool(image, "setTemplate:", true);
        }

        SendSize(image, "setSize:", widthPt, heightPt);
        return image;
    }

    /// <summary>Creates (once) an NSObject subclass exposing the given selectors, implemented by unmanaged function pointers.</summary>
    public static nint RegisterClass(string name, params (string Selector, string TypeEncoding, IntPtr Imp)[] methods)
    {
        var cls = objc_allocateClassPair(GetClass("NSObject"), name, 0);
        if (cls == 0)
        {
            return GetClass(name);
        }

        foreach (var (selector, typeEncoding, imp) in methods)
        {
            class_addMethod(cls, Sel(selector), imp, typeEncoding);
        }

        objc_registerClassPair(cls);
        return cls;
    }

    public static nint Send(nint receiver, string selector) => objc_msgSend(receiver, Sel(selector));

    public static nint Send1(nint receiver, string selector, nint arg) => objc_msgSend1(receiver, Sel(selector), arg);

    // CGFloat arguments travel in d0 on arm64, not x2, so integer overloads cannot be reused.
    public static nint SendD1(nint receiver, string selector, double arg) => objc_msgSendD1(receiver, Sel(selector), arg);

    public static nint Send2(nint receiver, string selector, nint a, nint b) => objc_msgSend2(receiver, Sel(selector), a, b);

    public static void SendV(nint receiver, string selector) => objc_msgSendV(receiver, Sel(selector));

    public static void SendV1(nint receiver, string selector, nint arg) => objc_msgSendV1(receiver, Sel(selector), arg);

    public static void SendV2(nint receiver, string selector, nint a, nint b) => objc_msgSendV2(receiver, Sel(selector), a, b);

    public static void SendV3(nint receiver, string selector, nint a, nint b, nint c) => objc_msgSendV3(receiver, Sel(selector), a, b, c);

    public static void SendBool(nint receiver, string selector, bool value) => objc_msgSendBool(receiver, Sel(selector), value ? (sbyte)1 : (sbyte)0);

    // NSSize is a 16-byte homogeneous double aggregate: passed in d0/d1 on arm64.
    public static void SendSize(nint receiver, string selector, double width, double height) => objc_msgSendSize(receiver, Sel(selector), width, height);

    [DllImport(LibObjC)]
    private static extern nint objc_getClass(string name);

    [DllImport(LibObjC)]
    private static extern nint sel_registerName(string name);

    [DllImport(LibObjC)]
    private static extern nint objc_allocateClassPair(nint superclass, string name, nint extraBytes);

    [DllImport(LibObjC)]
    private static extern void objc_registerClassPair(nint cls);

    [DllImport(LibObjC)]
    private static extern bool class_addMethod(nint cls, nint name, IntPtr imp, string types);

    [DllImport(LibObjC)]
    private static extern nint objc_retain(nint obj);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern nint objc_msgSend(nint receiver, nint selector);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern nint objc_msgSend1(nint receiver, nint selector, nint arg);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern nint objc_msgSendD1(nint receiver, nint selector, double arg);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern nint objc_msgSend2(nint receiver, nint selector, nint a, nint b);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern void objc_msgSendV(nint receiver, nint selector);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern void objc_msgSendV1(nint receiver, nint selector, nint arg);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern void objc_msgSendV2(nint receiver, nint selector, nint a, nint b);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern void objc_msgSendV3(nint receiver, nint selector, nint a, nint b, nint c);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern void objc_msgSendBool(nint receiver, nint selector, sbyte value);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern void objc_msgSendSize(nint receiver, nint selector, double width, double height);
}
