# Subsystem Template

Validated against commit: 8618f167efb6ed4f89b7fe60b69a25dd4da53fd1  
Last updated: 2025-12-28  
Branch: docs/refresh-index-subsystems

Fill each section with concise, technician-focused details. Use TODO/VERIFY where validation is incomplete and cite evidence (file/class/method).

- **Purpose** — What the subsystem does and which surfaces (dash/tab/exports) it feeds.
- **Inputs (source + cadence)** — Telemetry or upstream signals, including polling interval or event latch.
- **Internal state** — Key fields, windows, or latches that influence behaviour.
- **Calculation blocks (high level)** — Ordered steps or algorithms that transform inputs to outputs.
- **Outputs (exports + logs)** — SimHub exports, UI bindings, and log tags/messages.
- **Dependencies / ordering assumptions** — Required init order, update ordering, or cross-subsystem contracts.
- **Reset rules** — What triggers resets and which state clears or persists.
- **Failure modes** — Known edge cases, safeguards, and how they are detected.
- **Test checklist** — Minimal validation steps (telemetry scenarios, UI toggles, log expectations).
