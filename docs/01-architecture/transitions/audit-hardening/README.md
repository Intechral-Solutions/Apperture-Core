# Audit hardening

Working record for **AUD-1**. This folder is not the current contract.

| Doc | Status |
|-----|--------|
| [aud1-design-gate.md](aud1-design-gate.md) | **AUD-1.0 design gate. Approved by the product owner on 2026-10-06, with conditions** (section 3.2: corrections AC-1 to AC-4, T-B database protection, provisional backfill benchmark). Section 3 records rulings OR-1 to OR-14. Covers:<br>- atomic audit persistence for every required mutation, with operation-lock commit verification;<br>- the `external` actor kind;<br>- attribution provenance and defect notices;<br>- event-time username evidence;<br>- a classification set frozen once complete;<br>- outcome-aware routine visibility, with historical blocked rows kept visible;<br>- legacy outcome provenance and structured details;<br>- the integrity guard and the optional privileged startup credential;<br>- retention, the page-by-page `UserRef` split, and the staged rollout.<br>No product decision is open. |
| [aud1-1-foundation-record.md](aud1-1-foundation-record.md) | **AUD-1.1 implementation record.** Reviewed and merged (PR #15). Records the delivered scope, the answers to I-1 to I-6 and I-8, the I-6 changes to the §11.5 and §15.3 tables, the OR-14 benchmark, and the work left to AUD-1.2 to AUD-1.6. |
| [aud1-2-identity-governance-record.md](aud1-2-identity-governance-record.md) | **AUD-1.2 implementation record.** Implemented on branch `aud-1.2-identity-governance-audit`, independently reviewed; in review as a pull request, not merged. Records the identity and governance writer conversion, decisions D1 to D50 (including the signed id_token policy, D50), the findings and owners (§8, with the IAM-10 and IAM-11 items in §8.1), the review (§10) and the policy follow-up (§11). |

Governing contract: [rc1-program-gate.md](../../rc1-program-gate.md), RC1 requirement 5, D-13 and section 6.5. Inherited rulings: the [IAM-9 design gate](../identity-foundation/iam9-design-gate.md) (R14 as amended by OR-2, R17, R18) and the [LST-1 design gate](../list-query-foundation/lst1-design-gate.md) (section 18). AUD-1.2 adds open findings to IAM-10 and IAM-11 (record §8.1, listed in the program gate §8). This set accepts or resolves none of the findings those epics or RCV-1 own.

When the AUD-1 packages ship, fold the outcome into an ADR and the architecture docs, then archive this set (see the [transitions index](../README.md)).
