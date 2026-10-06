# Identity foundation

Working record for **IAM-9**. This folder is not the current contract.

| Doc | Status |
|-----|--------|
| [iam9-design-gate.md](iam9-design-gate.md) | **IAM-9.0 design gate, approved by the product owner with amendments (2026-10-03).** Username, session and actor identity, user reference, user search. **Status (2026-10-06):** IAM-9.1 to IAM-9.5 are merged to `master` (IAM-9.5: PR #10, `2b5bb52`), and the provisioning-result correction merged as PR #11 (`962abb3`). **IAM-9 is complete and its closure is accepted.** |
| [IAM-9 closure audit](../../../audits/iam9-identity-foundation-closure.md) | **Closure accepted** (§7), after PR #11 merged and post-merge CI was green. The audit found one approved §22 row unmet at `2b5bb52` (the assigned username on Entra provisioning results); the finding and the correction evidence are kept as history. This is not RC1 completion and not risk acceptance. The findings carried to IAM-10, AUD-1 and IAM-11 stay open under their owners (gate §29; closure audit §4, §7.3). Next Track A body: AUD-1.0. |

User lifecycle, disable, decommission and credential recovery are IAM-11, not this set. Context-scoped identity is not part of this set.
