# DSH session migration

These scripts move one DeepSeek Harness session from its old workspace to another
workspace **after DSH has fully exited**. DSH keeps the session log open while it
runs, so rewriting it live can lose messages.

## Files

- `Migrate-DshSession.ps1` — offline migration. Refuses to run while DSH is alive.
  Backs up `workspace.json`, the session projection cache and the session directory,
  then moves them and rewrites the session header `cwd`. Rolls back on failure.
- `Complete-DshSessionMigration.ps1` — waits for DSH to exit, runs the migration,
  then restarts DSH. Used by the scheduled task below.
- `migrate-dsh-session.mjs` — Node helper (zstd + JSON rewrite). Requires `fzstd`
  in `scripts/node_modules` (`npm install` in this folder).

## Dry run

```powershell
.\Migrate-DshSession.ps1 -SessionId $env:DSH_SESSION_ID -TargetPath D:\bored\RTools -DryRun
```

## Manual run

1. Quit DeepSeek Harness completely from the tray (not just close the window).
2. Run:

```powershell
powershell -ExecutionPolicy Bypass -File .\Migrate-DshSession.ps1 `
  -SessionId $env:DSH_SESSION_ID -TargetPath D:\bored\RTools
```

3. Reopen DeepSeek Harness; the session should be listed under `RTools`.

## Scheduled task

`RTools-DSH-Session-Migration` is a one-time Task Scheduler task. It starts
`Complete-DshSessionMigration.ps1`, waits up to 120 minutes for DSH to exit, then
migrates and restarts DSH. Log: `%TEMP%\rtools-dsh-migration.log`.

To remove it:

```powershell
Unregister-ScheduledTask -TaskName RTools-DSH-Session-Migration -Confirm:$false
```
