# Audit hardening

Working record for **AUD-1**. This folder is not the current contract.

| Doc | Status |
|-----|--------|
| [aud1-design-gate.md](aud1-design-gate.md) | **AUD-1.0 design gate. Approved by the product owner on 2026-10-06, with conditions** (section 3.2: corrections AC-1 to AC-4, T-B database protection, provisional backfill benchmark). Section 3 records rulings OR-1 to OR-14. Covers:<br>- atomic audit persistence for every required mutation, with operation-lock commit verification;<br>- the `external` actor kind;<br>- attribution provenance and defect notices;<br>- event-time username evidence;<br>- a classification set frozen once complete;<br>- outcome-aware routine visibility, with historical blocked rows kept visible;<br>- legacy outcome provenance and structured details;<br>- the integrity guard and the optional privileged startup credential;<br>- retention, the page-by-page `UserRef` split, and the staged rollout.<br>No product decision is open. **AUD-1.1 is next and has not started.** |

Governing contract: [rc1-program-gate.md](../../rc1-program-gate.md), RC1 requirement 5, D-13 and section 6.5. Inherited rulings: the [IAM-9 design gate](../identity-foundation/iam9-design-gate.md) (R14 as amended by OR-2, R17, R18) and the [LST-1 design gate](../list-query-foundation/lst1-design-gate.md) (section 18). Open findings owned by IAM-10, IAM-11 and RCV-1 are not changed by this set.

When the AUD-1 packages ship, fold the outcome into an ADR and the architecture docs, then archive this set (see the [transitions index](../README.md)).
