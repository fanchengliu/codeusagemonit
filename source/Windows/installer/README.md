# codeusagemonit setup.exe

Inno Setup 6 script for the Windows installer. The portable zip is unchanged: it has no `installed.txt`, and the app keeps using a `data` folder next to the executable.

## What the installer does

- Per-user by default (no administrator). The wizard can switch to “all users”, which asks for administrator rights.
- Directory page, default `{autopf}\codeusagemonit`:
  - current user → `%LOCALAPPDATA%\Programs\codeusagemonit`
  - all users → `C:\Program Files\codeusagemonit`
  - any other folder or drive is allowed, including a drive root.
- Start menu shortcut.
- Optional desktop shortcut (off by default).
- Optional “start with Windows” (off by default). Same registry value the app’s own 开机自启 switch uses: `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\codeusagemonit` = `"<install>\codeusagemonit.exe" --background`.
- Optional user PATH entry for the install folder so `codeusage` works in a new terminal (on by default). The existing PATH string is edited in place and its registry type (`REG_SZ` or `REG_EXPAND_SZ`) is kept. Uninstall removes only that entry. A pre-existing trailing `;` stays, and the rest of the string is not trimmed or rebuilt.
- Registers an uninstaller under Settings → Apps. Program files are removed. The uninstaller then asks before deleting user data; **No** is the default. A silent uninstall (`unins000.exe /SILENT`) keeps user data. A directory junction is not deleted (Scoop’s persist link). A `data` folder on a drive root is not offered for deletion either.
- Upgrades use the same AppId and previous directory, replace program files, and do not touch `%LOCALAPPDATA%\codeusagemonit`.
- Before copying files it asks a copy running from that folder to `--quit`, then force-closes only processes whose path is inside the install folder.

## Where data is stored

| How you run it | Settings, keys, caches |
| --- | --- |
| `setup.exe` | `%LOCALAPPDATA%\codeusagemonit` |
| Zip, Scoop, `install.ps1` | `<folder of the exe>\data` |
| `portable.txt` next to the exe | `<folder of the exe>\data`, even if that folder was installed |
| `CODEUSAGEMONIT_DATA` | that path, unless `portable.txt` is present |
| `--demo` | `<folder of the exe>\verification\demo-data` (never the real data) |

`setup.exe` writes `installed.txt` into the install folder and `HKCU\Software\codeusagemonit\InstallPath`. Either one selects the per-user directory, which is what makes a read-only Program Files install work for every Windows user (`installed.txt` is in the program folder; the registry value is only for the user who ran setup).

The first launch of an installed copy copies an existing `<exe>\data` into `%LOCALAPPDATA%\codeusagemonit` when that user folder is still empty, then deletes the old folder. A directory junction (Scoop’s `persist` link) is copied and **not** removed. If the user folder already has files, the copy beside the exe is left alone.

## Build on Windows

Requires Windows 10 or 11 x64 and [Inno Setup 6.3 or newer](https://jrsoftware.org/isdl.php). No Visual Studio. The Simplified Chinese wizard text is `ChineseSimplified.isl` next to `codeusagemonit.iss` (see `THIRD-PARTY-NOTICES.md`). Inno Setup's own `Languages` folder is not used for it. `x64compatible` needs 6.3. The script calls `CopyFile` on 6.4 and newer, and `FileCopy` on 6.3, which is the only name that version has. The translation matches the message names from Inno Setup 6.5 through 7.1, so those versions compile it with no language warnings. 6.3 and 6.4 still compile: a message this file does not define falls back to English, and a message name newer than that compiler is ignored.

```powershell
git clone https://github.com/fanchengliu/codeusagemonit.git
cd codeusagemonit
.\source\Windows\build.ps1 -Installer
.\codeusagemonit.exe --self-test
```

`-Installer` compiles the two executables, then writes these next to them (the repo root, unless you pass `-OutputDirectory`):

- `codeusagemonit-setup-1.3.0.exe` (version comes from `AssemblyVersion` in `source\Windows\App.cs`)
- `codeusagemonit-1.3.0-win-x64.zip` (portable; no `installed.txt` beside the exe). The zip includes `source/` so the program can be rebuilt, except `source/Windows/VERIFICATION.md`. That file is a maintainer checklist and is removed while the zip is staged. `setup.exe` never installs `source/` or `VERIFICATION.md`; it does install `使用说明.md`.
- `SHA256SUMS.txt`

`.\source\Windows\build.ps1` without `-Installer` still only builds the executables.

GitHub Actions workflow `.github/workflows/release.yml` runs the same command on `windows-latest`. Pushing a tag `v1.3.0` (it must match `AssemblyVersion`) creates the release. **Actions → Build Windows release → Run workflow** with “publish” checked uploads `codeusagemonit-setup-<version>.exe` onto an existing release and does not replace a zip that is already published (the Scoop manifest hashes that zip). A push to `main` that changes the packaged files (source, `使用说明.md`, README, licenses, `install.ps1` or the workflow itself) runs the same build without publishing, and checks that the zip ships the repository's `使用说明.md` unchanged and no `VERIFICATION.md`.

## Test on Windows

SmartScreen will warn; the executable is not code-signed. Choose More info → Run anyway.

1. `.\codeusagemonit.exe --self-test` exits 0. The new checks cover portable vs installed paths, migration into an empty folder, and a directory junction that is copied but not deleted.
2. Run `codeusagemonit-setup-1.3.0.exe`. Confirm the directory page opens on `%LOCALAPPDATA%\Programs\codeusagemonit` and that Browse can select another drive. Leave PATH on, desktop and startup off. Install.
3. Start menu opens the app. A new terminal runs `codeusage version` and `codeusage help` prints `数据目录` under `%LOCALAPPDATA%\codeusagemonit`. Settings → 打开数据目录 opens that folder. The install folder contains `installed.txt` and no `data` folder.
4. Change a setting and save. `%LOCALAPPDATA%\codeusagemonit\settings.json` updates.
5. Run setup again (upgrade). The same setting is still there.
6. Settings → Apps → codeusagemonit → Uninstall, and choose **No**. The install folder and the PATH entry are gone; `%LOCALAPPDATA%\codeusagemonit` remains. Install again and confirm the setting is still there. Uninstall again and choose **Yes**; that folder is removed.
7. Unzip `codeusagemonit-1.3.0-win-x64.zip` to `D:\Tools\codeusagemonit` and run it. `D:\Tools\codeusagemonit\data\settings.json` is created. `%LOCALAPPDATA%\codeusagemonit` is not used.
8. With that portable copy quit, run setup and choose `D:\Tools\codeusagemonit` as the folder. After the app starts, the old `data` folder has moved to `%LOCALAPPDATA%\codeusagemonit` and the setting survived.
9. Create `portable.txt` in the install folder and start the app again. New settings go to `<install>\data` (the per-user folder is left as it was).
10. Run setup, choose “all users”, and install under `C:\Program Files\codeusagemonit`. The app still saves to `%LOCALAPPDATA%\codeusagemonit` (no access-denied error).
11. Install with “start with Windows” checked. `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\codeusagemonit` is `"<install>\codeusagemonit.exe" --background`. Uninstall removes it. Also turn 开机自启 on inside the app and confirm uninstall removes that value when it points at this install.
12. Silent install (keeps the defaults: PATH on, desktop and startup off):

```powershell
.\codeusagemonit-setup-1.3.0.exe /CURRENTUSER /DIR="$env:LOCALAPPDATA\Programs\codeusagemonit" /SILENT
& "$env:LOCALAPPDATA\Programs\codeusagemonit\unins000.exe" /SILENT
```

The silent uninstall does not delete `%LOCALAPPDATA%\codeusagemonit`. Add `/LOG` to either command to write a log under `%TEMP%`.
