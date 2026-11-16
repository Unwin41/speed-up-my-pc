# Speed up my PC

**English** · [Türkçe](README.tr.md)

**One button. No ads. Nothing you need gets deleted.**

A tiny (under 100 KB) Windows app for anyone who has ever asked, "Why is my PC so slow, and where did my disk space go?"

<!-- TODO: add demo.gif showing the click -->

## How to use it

1. Download `SpeedUpMyPC.exe` from [Releases](../../releases).
2. Double-click it and say **Yes** when Windows asks for permission (it needs that to clean Windows' own temp folders).
3. Press **Speed up my PC**.

Windows may show **"Windows protected your PC"** because this app is new and not signed. Click **More info**, then **Run anyway**. The full source code is right here, so you can check exactly what it does.

## What it does

- **Removes temporary files that Windows recreates on its own:** your temp folder, the Windows temp folder, old Windows Update downloads, the Delivery Optimization cache, and the thumbnail cache. Files changed in the last 24 hours are left alone, in case something is using them.
- **Suggests startup apps to turn off**, like Spotify, Steam, or game launchers, that slow down your PC when it boots. You choose, nothing is uninstalled, and **Undo startup changes** brings them all back. It works the same way as Task Manager's Startup tab.

Two more tools sit at the bottom of the window. Both only **show** you things:

- **Find duplicate and large files.** It searches the folders you pick (Desktop, Documents, Downloads, Pictures, Videos and Music to start with) for files with exactly the same content, and for large files that haven't changed in a long time. You set how large and how old. It never deletes, moves or changes a file: **Show in folder** opens Explorer, and you decide. Links, junctions and online-only OneDrive files are skipped, not followed or downloaded. Anything it couldn't open is listed under **Skipped**, with the reason.
- **Installed apps.** It lists your apps by size. When an app doesn't report its size, the app measures its install folder. It doesn't show which apps you rarely use, because Windows doesn't keep a reliable "last used" date for desktop apps, and the app won't guess. **Uninstall in Windows Settings** opens Windows' own Installed apps page and copies the app's name for the search box. The uninstalling happens there.

The app speaks English and Turkish and picks your Windows language on its own. It also follows Windows' light or dark mode. To choose for yourself, open **Settings** at the bottom right.

## What it never does

- It never touches your documents, photos, Downloads, Recycle Bin, or browser data.
- It never deletes the duplicates or large files it finds, and it never uninstalls an app. It only lists them.
- It never touches drivers, antivirus, or any startup app it doesn't recognize. Only apps on a [short, readable list](src/SpeedUpMyPC/SafeStartupList.cs) are ever suggested.
- No ads, no tracking, no internet connection, no installer.

## Build it yourself

```powershell
./build.ps1
```

No SDK needed. It uses the C# compiler that already comes with Windows. The app ends up in `bin/SpeedUpMyPC.exe`.

## Add a language

1. Copy [`src/SpeedUpMyPC/lang/en.txt`](src/SpeedUpMyPC/lang/en.txt) to a new file named after the language code, for example `de.txt` or `pt-BR.txt`.
2. Translate the text after each `=` and the `@name` line. Leave `{0}`, `{1}` and `\n` as they are.
3. Build. The new language shows up in **Settings**, and it's picked automatically on Windows set to that language. Any line you skip appears in English.

## License

MIT
