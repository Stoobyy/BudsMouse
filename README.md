# Buds Mouse

Use your Galaxy Buds3 Pro as a head-controlled mouse on Windows 11.

- Small head movements move the cursor.
- A strong upward nod and return gives a left click.
- A strong downward nod and return gives a right click.
- Calibrate your head directions and adjust sensitivity in the app.

## Run

Requires Windows 11, paired Galaxy Buds3 Pro, and the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```powershell
dotnet run
```

Close Samsung Galaxy Buds or GalaxyBudsClient first so this app can use the Bluetooth connection.

## Set up

1. Wear both earbuds and click **Connect earbuds**.
2. Click **Look straight**, face forward, and hold for 3 seconds.
3. Click **Turn right**, turn right about 20 degrees, and hold for 3 seconds.
4. Return to forward, click **Look up**, then look up about 20 degrees and hold for 3 seconds.
5. Face forward and press **F10** to start. Try movement with nod clicks off first.
6. Enable **nod clicks** when ready. Return toward neutral after each deliberate nod to click.

| Key | Action |
| --- | --- |
| F10 | Start / pause |
| Esc | Emergency pause |
| F8 | Recenter and pause |

Return your head to neutral to stop moving. Recalibrate after adjusting the earbuds. Clicks start disabled; saved preferences include sensitivity, not calibration.

This is an experimental prototype using Samsung's undocumented Bluetooth protocol. Tested with one Buds3 Pro setup. Tracking can drift; use F8 to recenter. It pauses if fresh motion stops. Dragging and scrolling are not implemented.

## Build

```powershell
dotnet build -o app
```

Run `app\BudsMouse.exe` and keep the files in that folder together. To run the motion-engine checks without controlling the mouse:

```powershell
app\BudsMouse.exe --self-test
```

## Thanks

Protocol research and inspiration:

- [SamsungBuds3pro_tracker](https://github.com/Volkov02/SamsungBuds3pro_tracker)
- [GalaxyBudsClient](https://github.com/timschneeb/GalaxyBudsClient)
