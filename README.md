# Appcopier (archived)

Active development has moved to **WinRestoreKit**:

https://github.com/nicolasestrem/WinRestoreKit

WinRestoreKit is the independently maintained and substantially rebuilt
successor to this fork. This repository remains available for its pull
requests, issues, release history and development record.

Existing Appcopier backups remain compatible with WinRestoreKit. The backup
format did not change: the `app\` directory, `backup_manifest.json` and every
key in it, module identifiers, snapshot and backup folder naming and `.reg`
file naming are all unchanged. Place `WinRestoreKit.exe` beside your existing
`app\` directory, or copy that directory beside the executable. Copy it rather
than experimenting on your only backup.

Appcopier binaries check update endpoints that do not know about WinRestoreKit,
so they will not offer it to you automatically. Download it once by hand from
the link above. See
[MIGRATION.md](https://github.com/nicolasestrem/WinRestoreKit/blob/main/MIGRATION.md)
for the full record of what changed and what did not.

This repository is not deleted and is not detached from its fork network, so
every existing link, issue and release stays reachable.

---

## Original README (historical)

The text below is the README as it stood before the move. It is kept unchanged
as a record and is no longer maintained.

# Appcopier
### Back up key things on your Windows PC, perform a reset or simply go back in time.

https://github.com/builtbybel/Appcopier/assets/57478606/7a713db3-31b4-426b-94a7-54aaac11bfe7

This small project is still in the making. It allows you to back up and restore your most important Windows 11 preferences and settings offline and locally. The app [mimics the new backup app of Windows 11](https://support.microsoft.com/en-us/windows/back-up-your-windows-pc-87a81f8a-78fa-456e-b521-ac0560e32338) -  which is part of the Windows 11 2023 Update (23H2) - but without the obligation of the cloud. I will certainly expand and enhance it over time.  I, for example, don't understand why one cannot uninstall the new (old) Windows backup app and why it is supposed to be a 'system component'.There is any way to opt out, not even via Group Policy configurable. Even on Enterprise devices but its an consumer targeted app!?  In my eyes, the entire Windows Backup app is essentially a facade, primarily designed to promote the use of OneDrive.  Although the Windows Backup app appears to be merely a front-end for the already 77 existing sync experiences around the OneDrive app. Maybe we can achieve better results with Appcopier.

How does Appcopier works? Quite simple! Only registry entries and/or associated folders and files of the respective area are exported. This process is very quick and lightweight, akin to the weight of a fly. So don't be surprised if the first backups fly through in the nanosecond range. For the future, I could envision an addition of a more dynamic option in the form of scripts/plugins, where even more complex things could be backed up.

The project might remind some of you of one of my first public projects - CloneApp. It's been a long time. I wrote CloneApp back in the day with Classic Visual Basic 6 and Delphi, and eventually, I abandoned its maintenance. 

I've written and tested **Appcopier on Windows 11**, but it should also run on Windows 10 (no guarantee from me, though). Give it a try! This is the first release, and there's more to come.

## Requirements

- Windows 11, 64-bit (Windows 10 should work, untested)
- Run as administrator. Backing up and restoring registry areas shells out to `regedit.exe`, which needs elevation.

No .NET install is required — the runtime is bundled into the executable. Appcopier moved from .NET Framework 4.8 to .NET 8 after v0.30.0, and rather than ask you to install a runtime, releases ship self-contained. That is why the download grew from about 1 MB to about 69 MB; it is still a single `.exe` you can just run.








