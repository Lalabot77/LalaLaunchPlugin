# Message Engine v1 (SimHub exports)

This plugin now exposes a v1 message engine driven by the v5 message catalog and signal mappings. Dashboards can bind to the following SimHub properties:

| Property | Description |
| --- | --- |
| `MSGV1.ActiveText_Lala` | Current message text chosen for the Lala dash (priority sorted). |
| `MSGV1.ActivePriority_Lala` | Priority label (`Low`/`Med`/`High`) for the Lala dash message. |
| `MSGV1.ActiveMsgId_Lala` | MsgId for the Lala dash message. |
| `MSGV1.ActiveText_Msg` | Current message text for the Msg dash. |
| `MSGV1.ActivePriority_Msg` | Priority label for the Msg dash message. |
| `MSGV1.ActiveMsgId_Msg` | MsgId for the Msg dash message. |
| `MSGV1.ActiveCount` | Number of active messages in the engine. |
| `MSGV1.LastCancelMsgId` | MsgId of the last message canceled via MsgCx. |
| `MSGV1.ClearAllPulse` | True for a short pulse when a double‑tap clear occurs. |
| `MSGV1.StackCsv` | Debug view of the stack in most-recently-shown order (`msgId|Priority;...`). |

Backwards compatibility: the legacy `MsgCxPressed` property is unchanged and MsgCx still routes to the old dash lanes. The v1 engine listens to the same button:

- Single press: cancels the last-shown message (falls back to the most recently updated if none shown).
- Repeated presses step back through the stack until empty.
- Double-tap within ~350 ms: clears all active messages (suppression rules still apply).

Migration tips
--------------
- Point existing dashboard “active message” labels at `MSGV1.ActiveText_Lala` (Lala dash) or `MSGV1.ActiveText_Msg` (Msg dash) to pick up the new stack-based selection.
- Bind any cancel/clear button to the existing `MsgCxPressed` trigger; no new inputs are required.
- Keep legacy `MSG.*` lanes intact for now; they remain exported but are no longer required for the new engine.
