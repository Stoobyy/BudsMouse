using System.Numerics;
using System.Runtime.InteropServices;

static class Program {
 public static readonly Guid Service=new("2e73a4ad-332d-41fc-90e2-16bef06523f2");
 [STAThread] static void Main(string[] args) {
  if(args.Contains("--self-test")){Engine.Tests();return;}
  ApplicationConfiguration.Initialize();Application.Run(new MainWindow());
 }
}
sealed class Sensor : IDisposable {
 CancellationTokenSource cancel=new(); Task? worker;
 public event Action<Quaternion,long>? Sample; public event Action<string>? Status; public event Action? Unsafe;
 public void Start(){worker=Task.Run(Run);}
 static ushort Crc(byte[] data){int c=0;foreach(byte b in data){c^=b<<8;for(int i=0;i<8;i++)c=((c<<1)^((c&32768)!=0?0x1021:0))&65535;}return (ushort)c;}
 static byte[] Frame(byte id,byte v){ushort c=Crc([id,v]);return [253,4,0,id,v,(byte)c,(byte)(c>>8),221];}
 void Run(){try{
 var search=new Native.Search{size=(uint)Marshal.SizeOf<Native.Search>(),authenticated=1,remembered=1,connected=1};var d=Native.Device.New();ulong mac=0;var h=Native.BluetoothFindFirstDevice(ref search,ref d);
 if(h==IntPtr.Zero)throw new IOException($"Bluetooth discovery failed ({Marshal.GetLastWin32Error()})");
 try{do{if(d.name.Contains("Buds3",StringComparison.OrdinalIgnoreCase)&&d.authenticated!=0){if(mac!=0)throw new IOException("Multiple Buds3 devices paired. Remove unused pairing first.");mac=d.address;}}while(Native.BluetoothFindNextDevice(h,ref d));}finally{Native.BluetoothFindDeviceClose(h);}
 if(mac==0)throw new IOException("Pair your Buds3 Pro in Windows Bluetooth settings first.");
 Status?.Invoke("Connecting to Samsung sensor service…");using var link=new Rfcomm(mac,0,()=>cancel.IsCancellationRequested);
 try{link.Send(Frame(195,0));Status?.Invoke("Connected • waiting for motion");var buf=new List<byte>();var rx=new byte[4096];long next=Environment.TickCount64+2000;
 while(!cancel.IsCancellationRequested){if(Environment.TickCount64>=next){link.Send(Frame(195,4));next=Environment.TickCount64+2000;}int n=link.Read(rx);if(n==0){Thread.Sleep(5);continue;}buf.AddRange(rx.Take(n));
 while(buf.Count>=3){if(buf[0]!=253){buf.RemoveAt(0);continue;}int size=(buf[1]|buf[2]<<8)&1023;if(size<3){buf.RemoveAt(0);continue;}if(buf.Count<size+4)break;byte[] p=buf.Take(size+4).ToArray();buf.RemoveRange(0,size+4);if(p[^1]!=221||Crc(p[3..^3])!=(p[^3]|p[^2]<<8)||(p[2]&32)!=0)continue;
 if(p[3]!=194||p.Length<8)continue;if(p[4] is 34 or 36 or 41){Unsafe?.Invoke();continue;}
 if(p[4]==32&&p.Length>=17){float[] q=Enumerable.Range(0,4).Select(i=>BitConverter.ToInt16(p,5+i*2)/10000f).ToArray();var quat=new Quaternion(q[1],q[2],q[3],q[0]);float norm=quat.Length();if(norm<.8||norm>1.2){Unsafe?.Invoke();continue;}Sample?.Invoke(Quaternion.Normalize(quat),Environment.TickCount64);}
 }}}
 finally{try{link.Send(Frame(195,1));}catch{}}
 }catch(Exception e){Status?.Invoke("Disconnected • "+e.Message);}finally{Unsafe?.Invoke();}}
 public void Dispose(){cancel.Cancel();}
}

sealed class Engine {
 public Quaternion Neutral=Quaternion.Identity;public Vector3 Right,Up;public bool HasNeutral,HasRight,HasUp;
 public bool Armed;public double Speed=30,Deadzone=2,ClickAngle=18,ClickSpeed=80;public bool Clicks=true;
 public double X,Y;public string Gesture="";Quaternion latest;long latestTime,previous;double oldY,smX,smY;int pending;long gestureStart;long lastClick=-10000;bool ready;double fx,fy;
 public void Pause(){Armed=false;pending=0;ready=false;smX=smY=fx=fy=0;}
 public void Center(){Neutral=latest;HasNeutral=true;Pause();}
 public void SetSample(Quaternion q,long t){latest=q;latestTime=t;}
 Vector3 Delta()=>Rotation(Quaternion.Conjugate(Neutral)*latest);
 public static Vector3 Rotation(Quaternion q){q=Quaternion.Normalize(q);if(q.W<0)q=new(-q.X,-q.Y,-q.Z,-q.W);float s=new Vector3(q.X,q.Y,q.Z).Length();return s<1e-6?Vector3.Zero:new Vector3(q.X,q.Y,q.Z)/s*(2*MathF.Atan2(s,q.W)*180/MathF.PI);}
 public void Learn(bool right){if(!HasNeutral)throw new Exception("Capture neutral first.");var v=Delta();if(v.Length()<8||v.Length()>50)throw new Exception("Turn 10–30 degrees from neutral, then capture.");if(right){Right=Vector3.Normalize(v);HasRight=true;}else{Up=Vector3.Normalize(v);HasUp=true;}if(HasRight&&HasUp&&Math.Abs(Vector3.Dot(Right,Up))>.7){HasUp=false;throw new Exception("Directions overlap. Recalibrate up with a straight upward nod.");}}
 public bool Fresh(long now)=>latestTime>0&&now-latestTime<250;
 public (int dx,int dy,int click) Tick(long now){double dt=previous==0?.02:Math.Clamp((now-previous)/1000.0,.001,.05);previous=now;
 if(!Fresh(now)){Pause();return (0,0,0);}if(!HasNeutral||!HasRight||!HasUp)return(0,0,0);
 var v=Delta();double dot=Vector3.Dot(Right,Up),a=Vector3.Dot(v,Right),b=Vector3.Dot(v,Up);X=(a-dot*b)/(1-dot*dot);Y=(b-dot*a)/(1-dot*dot);double velocity=(Y-oldY)/dt;oldY=Y;
 if(!Armed)return(0,0,0);int click=0;
 if(Math.Abs(Y)<ClickAngle*.35 && pending==0)ready=true;
 if(Clicks&&ready&&now-lastClick>900&&Math.Abs(Y)>=ClickAngle&&Math.Abs(velocity)>=ClickSpeed&&Math.Sign(velocity)==Math.Sign(Y)){pending=Y>0?1:2;gestureStart=now;ready=false;}
 if(pending!=0){if(now-gestureStart>1200){pending=0;ready=false;Gesture="Nod timed out";}else if(Math.Abs(Y)<ClickAngle*.35){click=pending;pending=0;lastClick=now;Gesture=click==1?"Left click":"Right click";}smX=smY=fx=fy=0;return(0,0,click);}
 if(Math.Abs(Y)>=ClickAngle*.7){smX=smY=0;return(0,0,0);}
 double dz(double value)=>Math.Sign(value)*Math.Max(0,Math.Abs(value)-Deadzone);
 double alpha=1-Math.Exp(-dt/.09);smX+=alpha*(dz(X)*Speed-smX);smY+=alpha*(-dz(Y)*Speed-smY);fx+=Math.Clamp(smX,-1000,1000)*dt;fy+=Math.Clamp(smY,-1000,1000)*dt;int dx=(int)fx,dy=(int)fy;fx-=dx;fy-=dy;return(dx,dy,click);
 }
 public static void Tests(){var e=new Engine();e.SetSample(Quaternion.Identity,100);e.Center();e.HasRight=e.HasUp=true;e.Right=Vector3.UnitZ;e.Up=Vector3.UnitY;e.Armed=true;e.Tick(100);e.SetSample(Quaternion.CreateFromAxisAngle(Vector3.UnitY,25*MathF.PI/180),120);var r=e.Tick(120);if(r.click!=0||r.dx!=0||r.dy!=0)throw new Exception("gesture freeze");e.SetSample(Quaternion.Identity,300);r=e.Tick(300);if(r.click!=1)throw new Exception("left click on return");e.SetSample(Quaternion.Identity,400);e.Tick(400);e.SetSample(Quaternion.CreateFromAxisAngle(Vector3.UnitY,-25*MathF.PI/180),1400);e.Tick(1400);e.SetSample(Quaternion.Identity,1500);if(e.Tick(1500).click!=2)throw new Exception("right click");e.Tick(2000);if(e.Armed)throw new Exception("stale disarm");File.WriteAllText("self-test-result.txt","PASS: nod direction, return-to-neutral click, cursor freeze, stale-data disarm.");}
}

