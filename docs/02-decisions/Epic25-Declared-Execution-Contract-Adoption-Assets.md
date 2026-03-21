# Epic 25: Declared execution-contract adoption assets

## Overview

Epic 25 improves author adoption by adding richer sample coverage and copyable handler patterns for declared execution contracts.

## Implemented

- Expanded HelloWorld examples to cover additional declared intent variants:
  - success withheld (`SuccessWithheld`)
  - failure inline diagnostics (`FailInline`)
  - failure withheld diagnostics (`FailWithheld`)
- Added focused sample-alignment tests to keep HelloWorld examples truthful and regression-safe.
- Added concise copyable snippets in module authoring guide for:
  - preferred declared success
  - preferred declared failure
  - supported compatibility path (`OperatorRunOutput`) as non-preferred.
- Added lightweight starter pattern doc:
  - `docs/05-modules/execution-contract-handler-patterns.md`

## Compatibility and scope

- No runtime semantic redesign.
- No persistence/API/UI changes.
- No hard-enforcement changes.
- Preferred/supported/discouraged taxonomy remains aligned with ADR-0023.
