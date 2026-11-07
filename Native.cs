using System.Runtime.InteropServices;
using System.ComponentModel;

static class Native
{
    [StructLayout(LayoutKind.Sequential)] public struct SystemTime { public ushort y,m,dow,d,h,min,s,ms; }
    [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)]
    public struct Device {
        public uint size; public ulong address; public uint deviceClass;
        public int connected, remembered, authenticated; public SystemTime seen, used;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=248)] public string name;
        public static Device New() => new() { size=(uint)Marshal.SizeOf<Device>(), name="" };
    }
    [StructLayout(LayoutKind.Sequential)] public struct Search {
        public uint size; public int authenticated, remembered, unknown, connected, inquiry;
        public byte timeout; public IntPtr radio;
    }
    [DllImport("bthprops.cpl", SetLastError=true)] public static extern IntPtr BluetoothFindFirstDevice(ref Search search, ref Device device);
    [DllImport("bthprops.cpl", SetLastError=true)] public static extern bool BluetoothFindNextDevice(IntPtr find, ref Device device);
    [DllImport("bthprops.cpl")] public static extern bool BluetoothFindDeviceClose(IntPtr find);
    [DllImport("bthprops.cpl")] public static extern uint BluetoothEnumerateInstalledServices(IntPtr radio, ref Device device, ref uint count, [Out] Guid[] services);
    [DllImport("bthprops.cpl", SetLastError=true)] public static extern IntPtr BluetoothFindFirstRadio(ref uint size, out IntPtr radio);
    [DllImport("bthprops.cpl")] public static extern bool BluetoothFindRadioClose(IntPtr find);
    [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr handle);
    [StructLayout(LayoutKind.Sequential, Pack=1)] public struct Address {
        public ushort family; public ulong address; public Guid service; public uint channel;
    }
    [DllImport("ws2_32.dll")] public static extern int WSAStartup(ushort version, byte[] data);
    [DllImport("ws2_32.dll")] public static extern int WSACleanup();
    [DllImport("ws2_32.dll")] public static extern UIntPtr socket(int family,int type,int protocol);
    [DllImport("ws2_32.dll")] public static extern int connect(UIntPtr socket,ref Address address,int length);
    [DllImport("ws2_32.dll")] public static extern int ioctlsocket(UIntPtr socket,uint command,ref uint value);
    [DllImport("ws2_32.dll")] public static extern int getsockopt(UIntPtr socket,int level,int option,out int value,ref int size);
    [DllImport("ws2_32.dll")] public static extern int setsockopt(UIntPtr socket,int level,int option,ref uint value,int size);
    [DllImport("ws2_32.dll")] public static extern int send(UIntPtr socket,byte[] data,int length,int flags);
    [DllImport("ws2_32.dll")] public static extern int recv(UIntPtr socket,byte[] data,int length,int flags);
    [DllImport("ws2_32.dll")] public static extern int closesocket(UIntPtr socket);
    [DllImport("ws2_32.dll")] public static extern int WSAGetLastError();
    [StructLayout(LayoutKind.Sequential)] public struct Poll { public UIntPtr socket; public short events,revents; }
    [DllImport("ws2_32.dll")] public static extern int WSAPoll(ref Poll poll,uint count,int timeout);
    [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int key);
    [StructLayout(LayoutKind.Sequential)] public struct Point { public int x,y; }
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x,int y);
    public static Exception Error(string action) { int e=WSAGetLastError(); return new IOException($"{action}: Winsock {e}: {new Win32Exception(e).Message}"); }
}

sealed class Rfcomm : IDisposable
{
    UIntPtr socket; bool started;
    public Rfcomm(ulong mac,uint channel,Func<bool> stopped) {
        if(Native.WSAStartup(0x202,new byte[512])!=0) throw new IOException("WSAStartup failed");
        started=true;
        socket=Native.socket(32,1,3);
        try {
            if(socket==UIntPtr.MaxValue) throw Native.Error("socket");
            uint yes=1;
            // Require authenticated RFCOMM; pairing is left to Windows Settings.
            if(Native.setsockopt(socket,3,unchecked((int)0x80000001),ref yes,4)!=0) throw Native.Error("SO_BTH_AUTHENTICATE");
            if(Native.ioctlsocket(socket,0x8004667e,ref yes)!=0) throw Native.Error("nonblocking");
            var addr=new Native.Address {family=32,address=mac,service=channel==0?Program.Service:Guid.Empty,channel=channel};
            if(Native.connect(socket,ref addr,30)!=0) {
                if(Native.WSAGetLastError()!=10035) throw Native.Error("connect");
                var deadline=Environment.TickCount64+15000;
                while(true) {
                    if(stopped()) throw new OperationCanceledException();
                    if(Environment.TickCount64>deadline) throw new TimeoutException("RFCOMM connect exceeded 15 seconds");
                    var poll=new Native.Poll {socket=socket,events=0x10};
                    int n=Native.WSAPoll(ref poll,1,100);
                    if(n<0) throw Native.Error("connect poll");
                    if(n==0) continue;
                    int len=4;
                    if(Native.getsockopt(socket,0xffff,0x1007,out int error,ref len)!=0) throw Native.Error("SO_ERROR");
                    if(error!=0) throw new IOException($"connect: Winsock {error}");
                    if((poll.revents & 0x10)==0) throw new IOException($"connect poll failure: {poll.revents:X}");
                    break;
                }
            }
        } catch { Dispose(); throw; }
    }
    public void Send(byte[] data) {
        int offset=0; long deadline=Environment.TickCount64+500;
        while(offset<data.Length) {
            var tail=data[offset..]; int n=Native.send(socket,tail,tail.Length,0);
            if(n>0) {offset+=n;continue;}
            if(n<0 && Native.WSAGetLastError()==10035 && Environment.TickCount64<deadline) {Thread.Sleep(5);continue;}
            throw Native.Error("send");
        }
    }
    public int Read(byte[] buffer) {
        int n=Native.recv(socket,buffer,buffer.Length,0);
        if(n==0) throw new IOException("Earbuds closed RFCOMM");
        if(n<0) { if(Native.WSAGetLastError()==10035) return 0; throw Native.Error("recv"); }
        return n;
    }
    public void Dispose() { if(started) { if(socket!=UIntPtr.MaxValue) Native.closesocket(socket); Native.WSACleanup();started=false; } }
}

