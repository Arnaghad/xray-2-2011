# Blender extension releases

## 0.3.2 — 2026-11-15 (B8)

- Packaged artist guide: Ukrainian first-export workflow, rig/Action order,
  exact/fit keys, units, locators, physics, SDK handoff and troubleshooting.
- Documentation uses installed Blender 5.3.0, current migration commands and
  the accepted policy retaining 23 zero-root clips as failed.
- Maintainer runbook covers packaging, read-only corpus, sample/full migration,
  resumption, output acceptance, dependencies and host failure diagnostics.
- SDK harness defaults to the new ZIP. Saved SDK DCC options require explicitly
  selecting it; the previous 0.3.1 ZIP and acceptance receipts are retained.
- ZIP member comparison: only `README.md` and `blender_manifest.toml` changed;
  every runtime Python member is byte-identical to 0.3.1.

The Windows Blender installer can fail to rename its extracted temporary
directory with WinError 5. B8 retains the initial failed CI/SDK evidence and a
filesystem probe showing one rename succeeded after 100 ms. The blocking process
is unknown; this documentation release does not fix Blender's installer.
[B8 acceptance](b8-acceptance.json) records final successful checks and limitations.

## Earlier checkpoints

0.3.1 is the accepted B5/B6/B7 package, SHA-256
`7fad0d09e5da24ab3c4782588f6523e8b613ad6284bc3108c08e67d905b788c6`.
[B7 acceptance](b7-acceptance.json) and the
[pipeline plan](../../../blender_pipeline_plan.md) retain historical behavior,
test counts and corpus outcomes. The removed 5.0.1 portable host is historical;
current Windows workflows use the installed Blender 5.3.0.
