# Identity foundation

Working record for **IAM-9**. This folder is not the current contract.

| Doc | Status |
|-----|--------|
| [iam9-design-gate.md](iam9-design-gate.md) | **IAM-9.0 design gate, approved by the product owner with amendments (2026-10-03).** Username, session and actor identity, user reference, user search. **Status (2026-10-05):** IAM-9.1 to IAM-9.5 are merged to `master` (IAM-9.5: PR #10, `2b5bb52`). IAM-9 is not complete. |
| [IAM-9 closure audit](../../../audits/iam9-identity-foundation-closure.md) | **Not ready to close.** One approved §22 row was unmet on `master` (the assigned username on Entra provisioning results). The correction is implemented on `iam-9-provisioning-username-result`, pending merge. Everything else is evidenced. Closure awaits the correction merge and owner acceptance. The findings carried to IAM-10, AUD-1 and IAM-11 stay open (gate §29; closure audit §4). |

User lifecycle, disable, decommission and credential recovery are IAM-11, not this set. Context-scoped identity is not part of this set.
