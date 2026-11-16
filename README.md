# Speed up my PC

**One button. No ads. Nothing you need gets deleted.**

A tiny (22 KB) Windows app for anyone who has ever asked, "Why is my PC so slow, and where did my disk space go?"

<!-- TODO: add demo.gif showing the click -->

## How to use it

1. Download `SpeedUpMyPC.exe` from [Releases](../../releases).
2. Double-click it and say **Yes** when Windows asks for permission (it needs that to clean Windows' own temp folders).
3. Press **Speed up my PC**.

Windows may show **"Windows protected your PC"** because this app is new and not signed. Click **More info**, then **Run anyway**. The full source code is right here, so you can check exactly what it does.

## What it does

- **Removes temporary files that Windows recreates on its own:** your temp folder, the Windows temp folder, old Windows Update downloads, the Delivery Optimization cache, and the thumbnail cache. Files changed in the last 24 hours are left alone, in case something is using them.
- **Suggests startup apps to turn off**, like Spotify, Steam, or game launchers, that slow down your PC when it boots. You choose, nothing is uninstalled, and **Undo startup changes** brings them all back. It works the same way as Task Manager's Startup tab.

## What it never does

- It never touches your documents, photos, Downloads, Recycle Bin, or browser data.
- It never touches drivers, antivirus, or any startup app it doesn't recognize. Only apps on a [short, readable list](src/SpeedUpMyPC/SafeStartupList.cs) are ever suggested.
- No ads, no tracking, no internet connection, no installer.

## Build it yourself

```powershell
./build.ps1
```

No SDK needed. It uses the C# compiler that already comes with Windows. The app ends up in `bin/SpeedUpMyPC.exe`.

## License

MIT
