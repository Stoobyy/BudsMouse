using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;

sealed class MainWindow : Form {
 Engine engine=new();Sensor? sensor;Quaternion current;long sampleTime;object gate=new();
 Label status=new(),live=new(),hint=new(),gesture=new();Button connect=new(),arm=new();System.Windows.Forms.Timer timer=new(){Interval=20};
 bool lastF8,lastF10,lastEscape;int calibration;long captureAt;string sensorStatus="Disconnected";volatile bool unsafeState;
 NumericUpDown speed=new(),dead=new(),angle=new(),force=new();CheckBox clicks=new();
 readonly string settings=Path.Combine(AppContext.BaseDirectory,"settings.json");
 [StructLayout(LayoutKind.Sequential)] struct MouseInput {public int x,y;public uint data,flags,time;public UIntPtr extra;}
 [StructLayout(LayoutKind.Explicit,Size=40)] struct Input {[FieldOffset(0)] public uint type;[FieldOffset(8)] public MouseInput mouse;}
 [DllImport("user32.dll",SetLastError=true)] static extern uint SendInput(uint count,Input[] input,int size);
 public MainWindow(){
 Text="Buds Mouse";ClientSize=new(760,760);MinimumSize=new(720,760);StartPosition=FormStartPosition.CenterScreen;BackColor=Color.FromArgb(18,22,31);ForeColor=Color.FromArgb(232,237,246);Font=new("Segoe UI",10);
 var root=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new(28),ColumnCount=1,RowCount=10};Controls.Add(root);
 root.RowStyles.Add(new(SizeType.Absolute,52));root.RowStyles.Add(new(SizeType.Absolute,52));root.RowStyles.Add(new(SizeType.Absolute,52));root.RowStyles.Add(new(SizeType.Absolute,60));root.RowStyles.Add(new(SizeType.Absolute,100));root.RowStyles.Add(new(SizeType.Absolute,54));root.RowStyles.Add(new(SizeType.Absolute,150));root.RowStyles.Add(new(SizeType.Absolute,48));root.RowStyles.Add(new(SizeType.Absolute,48));root.RowStyles.Add(new(SizeType.Percent,100));
 root.Controls.Add(new Label{Text="Buds Mouse",Font=new("Segoe UI",24,FontStyle.Bold),AutoSize=true});
 status.Text="Disconnected • close Galaxy Buds before connecting";status.Dock=DockStyle.Fill;root.Controls.Add(status);
 var buttons=new FlowLayoutPanel{Dock=DockStyle.Fill};root.Controls.Add(buttons);Style(connect,"Connect earbuds",()=>Connect());buttons.Controls.Add(connect);Style(arm,"Start control · F10",()=>Toggle());buttons.Controls.Add(arm);var pause=new Button();Style(pause,"Pause · Esc",()=>Pause("Paused"));buttons.Controls.Add(pause);
 live.Dock=DockStyle.Fill;live.Font=new("Consolas",12);root.Controls.Add(live);
 var cal=new FlowLayoutPanel{Dock=DockStyle.Fill};root.Controls.Add(cal);var neutral=new Button();Style(neutral,"1. Look straight",()=>BeginCapture(1));cal.Controls.Add(neutral);var right=new Button();Style(right,"2. Turn right",()=>BeginCapture(2));cal.Controls.Add(right);var up=new Button();Style(up,"3. Look up",()=>BeginCapture(3));cal.Controls.Add(up);var center=new Button();Style(center,"Recenter · F8",()=>{if(engine.Fresh(Environment.TickCount64)){engine.Center();hint.Text="Recentered. Press F10 when ready.";}});cal.Controls.Add(center);
 hint.Text="Wear both buds. Connect, then calibrate each direction.\nEach capture waits 3 seconds for you to settle into the pose.";hint.Dock=DockStyle.Fill;root.Controls.Add(hint);
 var sliders=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=4,RowCount=3};root.Controls.Add(sliders);for(int i=0;i<4;i++)sliders.ColumnStyles.Add(new(SizeType.Percent,25));
 AddNumber(sliders,"Cursor speed",speed,5,80,30,0);AddNumber(sliders,"Dead zone (°)",dead,0,10,2,1);AddNumber(sliders,"Nod angle (°)",angle,10,40,18,2);AddNumber(sliders,"Nod speed (°/s)",force,30,250,80,3);
 clicks.Text="Enable nod clicks";clicks.AutoSize=true;clicks.Checked=false;sliders.Controls.Add(clicks,0,2);sliders.SetColumnSpan(clicks,2);var save=new Button();Style(save,"Save preferences",Save);sliders.Controls.Add(save,2,2);sliders.SetColumnSpan(save,2);
 gesture.Dock=DockStyle.Fill;gesture.ForeColor=Color.FromArgb(113,219,180);root.Controls.Add(gesture);
 root.Controls.Add(new Label{Text="F10 start/pause   •   F8 recenter   •   Esc emergency pause",Dock=DockStyle.Fill,Font=new("Segoe UI",11,FontStyle.Bold)});
 root.Controls.Add(new Label{Text="Turn or tilt slightly to move; return to neutral to stop. Strong up → left click; strong down → right click. Clicks happen on return. Start with clicks off.\n\nSettings apply to this session. Calibration must be repeated after reconnecting or adjusting the earbuds.",Dock=DockStyle.Fill});
 LoadSettings();timer.Tick+=(_,_)=>Tick();timer.Start();FormClosing+=(_,_)=>{engine.Pause();timer.Stop();sensor?.Dispose();};
 }
 void Style(Button b,string text,Action action){b.Text=text;b.AutoSize=true;b.Height=36;b.FlatStyle=FlatStyle.Flat;b.BackColor=Color.FromArgb(35,45,62);b.ForeColor=ForeColor;b.Margin=new(0,0,10,10);b.Padding=new(8,4,8,4);b.Click+=(_,_)=>{try{action();}catch(Exception e){hint.Text=e.Message;}};}
 void AddNumber(TableLayoutPanel p,string title,NumericUpDown n,int min,int max,int value,int col){p.Controls.Add(new Label{Text=title,AutoSize=true},col,0);n.Minimum=min;n.Maximum=max;n.Value=value;n.Width=125;n.BackColor=Color.FromArgb(35,45,62);n.ForeColor=ForeColor;p.Controls.Add(n,col,1);}
 void Connect(){if(sensor!=null){sensor.Dispose();sensor=null;engine=new();connect.Text="Connect earbuds";Pause("Disconnected");return;}
 engine=new();sampleTime=0;unsafeState=false;sensor=new();sensor.Sample+=(q,t)=>{lock(gate){current=q;sampleTime=t;}};sensor.Status+=s=>{lock(gate)sensorStatus=s;};sensor.Unsafe+=()=>unsafeState=true;sensor.Start();connect.Text="Disconnect";hint.Text="Waiting for live motion…";
 }
 void BeginCapture(int step){if(!engine.Fresh(Environment.TickCount64))throw new Exception("Connect and wait for live motion first.");engine.Pause();calibration=step;captureAt=Environment.TickCount64+3000;hint.Text=step==1?"Look straight ahead and hold…":step==2?"Turn your head RIGHT about 20 degrees and hold…":"Look UP about 20 degrees and hold…";}
 void Toggle(){if(engine.Armed){Pause("Paused");return;}if(!engine.HasNeutral||!engine.HasRight||!engine.HasUp||!engine.Fresh(Environment.TickCount64)){hint.Text="Complete all three calibration steps with live motion first.";return;}engine.Pause();engine.Armed=true;hint.Text="Control active. Esc pauses immediately.";}
 void Pause(string reason){engine.Pause();calibration=0;hint.Text=reason;}
 bool Edge(int key,ref bool old){bool down=(Native.GetAsyncKeyState(key)&0x8000)!=0;bool result=down&&!old;old=down;return result;}
 void Tick(){long now=Environment.TickCount64;lock(gate){engine.SetSample(current,sampleTime);status.Text=sensorStatus;}
 if(unsafeState){unsafeState=false;Pause("Paused: earbuds disconnected, removed, or sensor needs attention.");}
 if(Edge(0x1b,ref lastEscape))Pause("Emergency pause");if(Edge(0x77,ref lastF8)&&engine.Fresh(now)){engine.Center();hint.Text="Recentered • paused";}if(Edge(0x79,ref lastF10))Toggle();
 if(calibration!=0){if(!engine.Fresh(now)){Pause("Calibration stopped: no fresh motion.");}else if(now>=captureAt){int step=calibration;calibration=0;try{if(step==1){engine.Center();engine.HasRight=engine.HasUp=false;}else engine.Learn(step==2);hint.Text=step==1?"Neutral captured. Now calibrate right.":step==2?"Right captured. Return to straight, then calibrate up.":"Calibration complete. Return to straight; press F10 to start.";}catch(Exception e){hint.Text=e.Message;}}}
 engine.Speed=(double)speed.Value;engine.Deadzone=(double)dead.Value;engine.ClickAngle=(double)angle.Value;engine.ClickSpeed=(double)force.Value;engine.Clicks=clicks.Checked;
 var result=engine.Tick(now);arm.Text=engine.Armed?"Pause control · F10":"Start control · F10";arm.BackColor=engine.Armed?Color.FromArgb(22,104,81):Color.FromArgb(35,45,62);
 live.Text=engine.Fresh(now)?$"LIVE   Right {engine.X,6:F1}°   Up {engine.Y,6:F1}°\nCalibration: {(engine.HasNeutral?"neutral ✓":"neutral —")}  {(engine.HasRight?"right ✓":"right —")}  {(engine.HasUp?"up ✓":"up —")}":"No fresh motion • control paused";
 if(engine.Armed){if(result.dx!=0||result.dy!=0){if(Native.GetCursorPos(out var p))Native.SetCursorPos(p.x+result.dx,p.y+result.dy);}if(result.click!=0){var down=new Input{mouse=new MouseInput{flags=result.click==1?2u:8u}};var up=new Input{mouse=new MouseInput{flags=result.click==1?4u:16u}};if(SendInput(2,[down,up],40)!=2){engine.Pause();hint.Text="Click injection failed. Control paused.";}}}
 gesture.Text=engine.Gesture;
 }
 void Save(){var values=new {speed=speed.Value,dead=dead.Value,angle=angle.Value,force=force.Value};File.WriteAllText(settings,JsonSerializer.Serialize(values));hint.Text="Preferences saved. Nod clicks always start disabled.";}
 void LoadSettings(){try{if(!File.Exists(settings))return;using var doc=JsonDocument.Parse(File.ReadAllText(settings));var v=doc.RootElement;speed.Value=Math.Clamp(v.GetProperty("speed").GetDecimal(),speed.Minimum,speed.Maximum);dead.Value=Math.Clamp(v.GetProperty("dead").GetDecimal(),dead.Minimum,dead.Maximum);angle.Value=Math.Clamp(v.GetProperty("angle").GetDecimal(),angle.Minimum,angle.Maximum);force.Value=Math.Clamp(v.GetProperty("force").GetDecimal(),force.Minimum,force.Maximum);}catch{hint.Text="Preferences could not be loaded; using defaults.";}}
}

