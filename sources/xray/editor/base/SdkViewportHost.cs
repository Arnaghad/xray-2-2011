using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace xray.editor_base;

// Vulkan owns a lease on SurfaceHandle, never on the replaceable WinForms HWND.
// Park the child before DestroyHandle; dock/DPI/RecreateHandle can then replace
// the managed parent without destroying a surface with GPU frames in flight.
public class SdkViewportHost : Panel
{
    public SdkViewportHost(){SetStyle(ControlStyles.Selectable,true);TabStop=true;}
    protected override bool IsInputKey(Keys keys)=> (keys&Keys.KeyCode) is Keys.Left or Keys.Right or Keys.Up or Keys.Down || base.IsInputKey(keys);
    private nint surface,parking,previousProc;
    private bool leased,disposeRequested;
    private readonly int owner=Environment.CurrentManagedThreadId;
    private delegate nint WindowProcedure(nint window,uint message,nint w,nint l);
    private WindowProcedure? procedure;
    [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern nint CreateWindowExW(uint extended,string type,string name,uint style,int x,int y,int width,int height,nint parent,nint menu,nint instance,nint data);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(nint window);
    [DllImport("user32.dll",SetLastError=true)] private static extern nint SetParent(nint window,nint parent);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint window,nint after,int x,int y,int width,int height,uint flags);
    [DllImport("user32.dll",EntryPoint="SetWindowLongPtrW")] private static extern nint SetWindowLongPtr(nint window,int index,nint value);
    [DllImport("user32.dll")] private static extern nint CallWindowProcW(nint proc,nint window,uint message,nint w,nint l);
    public nint SurfaceHandle {get {Check();if(disposeRequested)throw new ObjectDisposedException(nameof(SdkViewportHost));EnsureSurface();return surface;}}
    public bool HasSurface=>surface!=0;
    public bool SurfaceLeased=>leased;
    private void Check(){if(owner!=Environment.CurrentManagedThreadId)throw new InvalidOperationException("Foreign viewport host owner.");}
    private void EnsureSurface()
    {
        _=Handle;if(surface!=0)return;
        parking=CreateWindowExW(0,"STATIC","SDK viewport parking",0x80000000,0,0,1,1,0,0,0,0);
        surface=CreateWindowExW(0,"STATIC","SDK Vulkan surface",0x50000000,0,0,Math.Max(1,ClientSize.Width),Math.Max(1,ClientSize.Height),Handle,0,0,0);
        if(parking==0 || surface==0){DestroySurface();throw new InvalidOperationException("Cannot create native viewport surface.");}
        procedure=(window,message,w,l)=>message==0x84?(nint)(-1):CallWindowProcW(previousProc,window,message,w,l); // HTTRANSPARENT: input belongs to the managed host.
        previousProc=SetWindowLongPtr(surface,-4,Marshal.GetFunctionPointerForDelegate(procedure));
        if(previousProc==0){DestroySurface();throw new InvalidOperationException("Cannot attach viewport input routing.");}
    }
    public void AcquireSurfaceLease(){Check();_=SurfaceHandle;if(leased)throw new InvalidOperationException("Viewport surface already leased.");leased=true;}
    public void ReleaseSurfaceLease(){Check();if(!leased)throw new InvalidOperationException("Viewport surface has no lease.");leased=false;if(disposeRequested)DestroySurface();}
    private void DestroySurface()
    {
        if(leased)throw new InvalidOperationException("Cannot destroy a leased Vulkan surface.");
        if(surface!=0){DestroyWindow(surface);surface=0;}
        if(parking!=0){DestroyWindow(parking);parking=0;}procedure=null;previousProc=0;
    }
    protected override void DestroyHandle()
    {
        if(surface!=0)SetParent(surface,parking);
        base.DestroyHandle();
    }
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if(surface!=0){SetParent(surface,Handle);SizeSurface();}
    }
    private void SizeSurface(){if(surface!=0 && IsHandleCreated)SetWindowPos(surface,0,0,0,ClientSize.Width,ClientSize.Height,0x14);}
    protected override void OnSizeChanged(EventArgs e){base.OnSizeChanged(e);SizeSurface();}
    // Explicit entry point also exercises the framework's real HWND recreation.
    public void RecreateViewportHandle(){Check();if(disposeRequested)throw new ObjectDisposedException(nameof(SdkViewportHost));RecreateHandle();}
    protected override void Dispose(bool disposing)
    {
        if(disposing){Check();disposeRequested=true;}
        base.Dispose(disposing);
        if(disposing && !leased)DestroySurface();
    }
}
