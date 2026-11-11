# IAM-10.2b WP5: independent security review brief

Write for a reviewer who has not seen this work. The record is [iam10-2b-wp5-closure-record.md](iam10-2b-wp5-closure-record.md); read its sections 1 to 3, 5 and 7 first. WP0 §10 makes this the "independent security review of the integrated implementation": WP1 to WP4B are accepted, and the question is whether the integrated whole meets the IAM-10.2b contract ([design gate](iam10-design-gate.md) §3.2 and §7, A10.2b.1 to A10.2b.17), not whether each package did.

## 1. What is under review

| | |
|---|---|
| Branch and head | `feat/iam10-2b-wp5`, local only. Code head `acf92841` (`src` `19d6c1b1`, `tests` `138b07b2`); the head is the documentation commit named in the hand-over message, which changes neither tree |
| Baseline | `1c8067cd` (the accepted IAM head). Review `git diff 1c8067cd acf92841 -- src tests docs/04-api` |
| Production changes (three) | `07fe0b54` SI-1 (set-password maps a busy gate to 409); `79e96109` A10.2b.12 (the status and the overview say the recovery codes are exhausted and name the remedy); `276584f7` review C4 (set-password lets an audit invariant failure through as an internal error) |
| Test-only changes | `03b9fe6f` and `9f553ad5` (the writer guard, two rounds); `f35d8cce` (the pair matrix); `b1f2e6d3` (the D-C and D-D measurement); `6f779f93` (CLI L-1); `3ddea2bf` (the acceptance clauses no package exercised, the per-family gate controls); `acf92841` (the MFA composition control: a sign-in reached without the host's MFA registration) |
| Under review | The integrated IAM-10.2b implementation as exercised by record §2 and §3; the three production changes; the writer guard; the MFA composition analysis (record §7); the measurement (record §4) |
| Not under review | Re-acceptance of WP1 to WP4B in isolation (reopen only on a concrete defect of the integrated tree); IAM-10.3, IAM-10.4, IAM-10.5, IAM-11; OPS-1; environment tooling |

## 2. Binding requirements

A10.2b.1 to A10.2b.17, with the preamble of §7 (both isolation modes, deterministic interleavings, mutation-checked against the gate's removal); the WP0 rulings D-1 to D-6 and departures E-1 and E-2; C-13-R; C-14; the decision record's rulings; the deferrals WP4A §7 (D-A to D-E) and WP1 review §2 (writer-guard hardening, R-2).

## 3. Questions

1. **Requirements to evidence (record §2).** For each of A10.2b.1 to A10.2b.17, does the named evidence test the clause as written, on the integrated tree? Find a clause, or a part of one, that no test exercises. Are the stated limits (§2.1: `sp_getapplock` -2 and -999 at unit level only; the command line's password set treated as out of category, not a covered unit) acceptable?
2. **Matrix completeness (record §3).** Find a pair of viability-affecting writers that can together violate an invariant and is not run in both orders, both modes, concurrently and sequentially, or a family whose arrangement does not make "either alone allowed, both together violating" true. Are the excluded writers (raisers, the sign-in policy, E-2, C-14) correctly excluded?
3. **Expected outcomes.** For F3 (revocation first lets the reduction commit), F7 (each second change decided on the first's commit) and C-14 × switch (an old code no longer valid once the switch commits), are the expected outcomes the serial ones under C-13-R, D-1, D-2, I-R3-1 and C-14, or did a test encode the implementation where the contract says otherwise? In particular: does "not blocked by a switch in progress" (decision record, C-14) allow a sign-in to wait on a code row the switch replaced, for the length of the switch's transaction?
4. **The gate controls (record §3.3, §8).** Most concurrent cases detect a removed gate through the harness's gate probe; the outcome-level controls are one per family under row versioning. Is that a sound reading of the preamble's "the test must fail with the gate removed"? Which concurrency cases survive the gate's removal on outcomes, and is each survival explained?
5. **A10.2b.12 (record §5.5).** Does the presentation now meet "says the codes are exhausted and names the remedy" on both surfaces? Is the condition right (enrolled, designated, no unused code), including the states where it must stay silent?
6. **Cross-composition (A10.2b.13).** F9 runs the emergency command through `EmergencyCli.RunAsync` (the production container, its own connection) in the test process, not a separate OS process. Is that sufficient, given that the lock is database-scoped and transaction-owned?
7. **Writer guard (record §5.1, §5.7).** After two rounds and eighteen probes, find a production writer shape of a viability input that the guard still misses and that a plausible change could introduce. Are the persistence-file, migration and concrete-type pins a sound closure of "a new store or a new write method"? Is the residual (§5.7) stated correctly?
8. **SI-1 and C4 (record §5.2, §5.6).** Was making them within WP5's contract (record §10.4), and is the mapping complete and correct for every gate-taking host endpoint?
9. **MFA composition (record §7, §5.9).** Is "production composition demonstrably safe" established, or is there a reachable composition (another host, a test-only path shipped, a configuration switch) in which `LocalLogin` would skip the second factor? M07 shows that a dropped registration makes the sign-in skip it silently rather than fail. Is the sign-in test of §5.9 a sufficient net until the constructor clean-up, and is deferring that clean-up acceptable?
10. **D-C and D-D.** Does the measurement answer the deferral's question, and is the 15 s hold of an affected sign-in correctly characterized as latency, not a security issue?
11. **Controls (record §8).** Are the 26 mutants real controls, each caught by a valid assertion on the wrong behaviour it introduces? Read the catches in the TRX files, not only the counts: M07's catch at `3ddea2bf` was an arrangement failure, which is why the run was repeated at `acf92841` (record §5.9).
12. **Full assembly (record §9).** Check the counts, the skipped and not-executed results, and the SQL execution proof of the whole `IntegrationTests` assembly.

## 4. Evidence available

The evidence archive and its manifest are named in the hand-over message and record §12: the clean build, every suite's TRX, the full integration run with its SQL markers, the honesty report, the probe and mutation runs with their harness and mutant definitions, the measurement output. Verify the manifest (CRLF, backslash paths: use PowerShell `Get-FileHash`) before relying on it.

## 5. Required output

`ACCEPT`, `ACCEPT WITH BOUNDED OBLIGATIONS` or `REJECT` at the exact commit; each new finding with a concrete scenario and its severity, kept separate from informational improvements; the exact scope of the acceptance; what was run. The D-C and D-D ruling is the owner's, not the reviewer's.
