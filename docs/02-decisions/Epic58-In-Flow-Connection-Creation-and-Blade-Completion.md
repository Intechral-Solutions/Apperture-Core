# Epic 58 — In-Flow Connection Creation and Binding Completion

## Status

Implemented

## Objective

Close the biggest remaining seam in the integration-centered operator workflow by allowing an operator to create a missing compatible connection **from within the integration binding flow** and bind it immediately, without leaving the integration detail experience.

This epic preserves the architectural decisions established in earlier epics:

- **Integrations** remain the center of authoring and remediation.
- **Connections** remain first-class managed platform entities.
- **Connector definitions** remain the source of truth for connection field shape.
- The platform does **not** introduce a shadow “quick connection” concept or any generic connector/client operation surface.

## Summary

Epic 58 extended the integration-centered setup flow so operators can remediate missing slot bindings in context.

The implementation adds:

- an ADR clarifying the operator-flow extension for in-context connection creation
- stricter binding eligibility logic so compatible connections are filtered by:
  - active state
  - connector definition presence
  - connector-factory support for the requested contract type, when resolvable
- management client support for creating first-class connections
- connector-definition field metadata projection for required-field handling
- integration-detail UI support for:
  - creating a connection from the binding flow
  - rendering a metadata-driven form from the connector definition
  - validating required fields
  - creating the connection
  - refreshing eligibility
  - auto-selecting the created connection when eligible
  - binding it without leaving the integration flow
- governance fixture and HTTP proof for the missing-connection remediation path

The resulting operator journey becomes:

1. create an integration
2. enter integration detail remediation flow
3. discover a missing required slot binding
4. create a compatible connection in context
5. bind it immediately
6. see readiness improve without leaving the integration-centered workflow

## Architectural Intent

This epic intentionally improves workflow coherence without changing platform ownership boundaries.

### What remains true

- A connection created in-flow is still a **normal first-class connection**
- Connector definition metadata still shapes the connection form
- Binding remains a normal integration-slot binding operation
- Readiness remains server-owned truth
- No new connector/client abstraction was introduced

### What changed

The operator no longer has to leave the integration flow just to create a missing dependency.

That means the platform is now better aligned with the intended control-plane story:

- integrations are the deployable/configurable unit
- missing dependencies can be remediated in integration context
- connection management remains truthful while becoming less fragmented

## Files Changed

### ADR / docs

- `docs/02-decisions/ADR-0033-in-flow-connection-creation-for-integration-binding.md`

### Control-plane eligibility / truthfulness

- `src/Apperture.ControlPlane/Apperture.ControlPlane.Security/BindingEligibilityService.cs`

### Management client

- `src/Apperture.ControlPlane/Apperture.Management.Client/IManagementClient.cs`
- `src/Apperture.ControlPlane/Apperture.Management.Client/ManagementClient.cs`

### Connector-definition metadata projection

- `src/Apperture.ControlPlane/Apperture.Management.Api/Controllers/ConnectorDefinitionsController.cs`

### UI

- `src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Integrations/IntegrationDetail.razor`

### Tests

- `tests/Apperture.UnitTests/Integration/BindingEligibilityServiceTests.cs`
- `tests/Apperture.IntegrationTests/ManagementHost/ManagementHostGovernanceFixture.cs`
- `tests/Apperture.IntegrationTests/ManagementHost/ManagementHostGovernanceHttpTests.cs`

## Operator-Facing Result

After Epic 58, an operator working through integration setup can now:

- see that a required slot is unbound
- understand which connector/connection shape is needed
- create a compatible connection directly from the binding context
- complete the bind immediately after creation
- remain inside the integration workflow
- observe readiness improve without awkward cross-navigation

This makes the setup/remediation flow meaningfully more coherent and reduces the need to mentally stitch together multiple disconnected pages.

## Implementation Notes

### 1. Eligibility truth was tightened

Eligible connections are no longer filtered only by loose connector-definition matching.

The eligibility service now also considers connector-factory support for the requested contract type when that can be resolved. This reduces the chance of presenting a connection as selectable when it is not actually usable for the requested slot contract.

### 2. Required-field projection was made explicit

Connector-definition field metadata now includes required-state projection for the UI path used during in-flow connection creation.

This supports honest metadata-driven validation rather than relying on implicit client assumptions.

### 3. In-flow create-and-bind stays on the real connection model

The in-flow creation experience uses the actual connection creation path and the resulting connection is treated as a normal platform connection. This avoids introducing a second-class shortcut model that would drift from the main connection architecture.

### 4. Readiness improvement is proven, not assumed

The governance HTTP test for the missing-connection remediation path exercises the full flow:

- create integration
- create connection
- confirm eligibility
- bind connection
- assert that readiness improves by reducing `missing_slot_binding` issues

## Verification

The following verification was reported for Epic 58:

- `dotnet build src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Apperture.Management.Ui.Client.csproj`
- `dotnet test tests/Apperture.UnitTests/Apperture.UnitTests.csproj --filter "BindingEligibilityServiceTests|Epic48ConnectionsSurfaceTests"`
- `dotnet test tests/Apperture.IntegrationTests/Apperture.IntegrationTests.csproj --filter "ManagementHostGovernanceHttpTests"`

Reported result:

- UI client build passed
- targeted unit tests passed
- governance HTTP tests passed
- lints on edited files passed

## Architectural Result

Epic 58 leaves the codebase in a stronger state with respect to the integration workflow.

### Integration-centered remediation including missing-connection creation

**Yes**

The integration detail flow can now remediate a missing connection dependency directly.

### Continued first-class connection model

**Yes**

Connections created in-flow remain normal managed platform connections.

### Connector-definition-driven in-context connection authoring

**Yes**

The in-flow form is shaped by connector definition metadata rather than ad hoc UI-only field definitions.

## What Epic 58 Did Not Do

This epic did **not**:

- redesign connector definitions
- redesign integrations
- redesign procedures
- introduce gateways
- create a shadow “quick connection” concept
- broaden into a full standalone connection-management overhaul
- claim that metadata completeness equals remote-system validity

That last point remains important: a connection can now be created and bound more coherently, but that does not yet prove that the connection actually works against the remote system.

## Follow-up Implication

Epic 58 removed the biggest workflow seam, but it also made the next trust gap more obvious:

- a connection can be complete enough to create and bind
- readiness can improve
- but the connection may still fail at runtime because credentials, endpoint, or remote access are wrong

That naturally leads to the next follow-up area:

- **connection verification / test-connect**
- verification-aware remediation guidance
- clearer operator confidence about whether a configured dependency has actually been proven against the remote system

## Conclusion

Epic 58 completed an important usability and truthfulness step for the control plane.

The platform now supports a much more coherent integration-centered setup journey:

- create the integration
- remediate missing bindings
- create missing connections in context
- bind immediately
- continue toward readiness, scheduling, and procedures

This improves operator flow without compromising the architectural boundaries established in the connector, connection, integration, and procedure redesign work.