# Build-protocol experiment tooling (DEV-1.3C)

Measurement tooling for the validation build-protocol experiment. **It adopts nothing**: the established protocol (`--no-incremental -m:1 -p:UseSharedCompilation=false --disable-build-servers`, a fresh `--artifacts-path`) is unchanged and remains the only mandatory procedure. The scripts exist so the experiment can be reproduced and reviewed; the results are in [dev1-3c-build-protocol-experiment.md](../../../docs/01-architecture/transitions/dev-loop/dev1-3c-build-protocol-experiment.md).

Requires PowerShell 7.2+, Git and the SDK named by `global.json`. Windows only (performance counters); Linux is not supported.

| Script | What it does |
|---|---|
| `Invoke-BuildTrial.ps1` | One measured clean trial of `Apperture.slnx` for configuration `A` (established), `B` (`-m:1`, servers on), `C` (`-m:4`, servers on), `D` (SDK defaults), or the supplementary `C0` / `D0` (no `--no-incremental`). Checks the tree, commit, environment and free memory first; writes the exact commands before running; times restore and build separately; samples available memory, paging, CPU and the build processes once a second; stops only its own build if available memory falls below 600 MiB; counts warnings and errors; looks for crash signals; shuts down only the build servers that this trial started; writes the assembly inventory. Refuses to start while a foreign build process is running (never touches one). |
| `Run-Pass.ps1` | Runs a list of trials in order. `-SamePathName S` builds every trial into the same output path and moves it aside afterwards (the strict assembly-identity control). |
| `Get-AssemblyInventory.ps1` | Ordinal-sorted inventory of `<artifacts>/bin` with raw SHA-256 and, for managed assemblies, a normalised hash (the module id, PE and debug-directory time stamps, the PDB path/id/checksum are zeroed). `-MaskRegexGeneratorHash` additionally zeroes the `<Name>F<64 hex>__` file-local-type hash, a diagnostic used to show that a difference comes from the output path. |
| `Compare-Inventories.ps1` | Raw-identical, normalised-identical and different files between two trials. |
| `Compare-MutationBuilds.ps1` | One mutation outcome under two build configurations in a disposable clone, without `Invoke-Mutation -BuildArgs`. |
| `Mutate-AllowList.ps1` | Mutation check of the build-argument allow-list controls in `validation/tests/Test-Validation.ps1` (section `buildargs`). |
| `Run-FirstRecord.ps1`, `Run-FirstRecordPass2.ps1` | The first toolkit-backed record beside the established procedure, on a bounded non-SQL scope, in a disposable standalone clone: both methods, `Compare-Trx -Strict`, and the managed-assembly comparison. Pass 2 names the one skipped test and uses an artifacts path of the same length as the established run's, because a different length changes PE section sizes. |

Trial output goes to the ignored `artifacts/dev13c/` folder; nothing here writes a tracked file.
