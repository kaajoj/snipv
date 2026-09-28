# Backlog

Ideas parked for "someday" - accepted in principle, not scheduled.

## Hotstrings (text expansion)

Typing a trigger like `!tel` in any application replaces it with the snippet
content: the app erases the typed trigger with backspaces and pastes the
snippet (reusing `SnippetInserterService`).

Decisions made so far:

- Trigger fires **immediately** after the last character of `!tel` - no
  space/enter terminator.
- A snippet would have either a chord shortcut **or** a `!word` trigger.
- Deferred mainly because it requires a global low-level keyboard hook
  (`SetWindowsHookEx(WH_KEYBOARD_LL)`), which makes the app see every keystroke
  system-wide - a privacy cost not worth paying yet.

Implementation notes for later:

- Translate keys to characters with `ToUnicodeEx` (keyboard-layout aware:
  Polish diacritics, Shift, AltGr).
- Mark own synthetic input (`GetMessageExtraInfo`) so pasting can't re-trigger
  a hotstring loop.
- Reset the typing buffer on mouse click / focus change / non-character keys.
- Keep the hook callback trivial (buffer append + lookup) and do the actual
  replacement outside the hook thread - a slow LL hook delays typing
  system-wide.
- Limitation: keystrokes in elevated (admin) windows never reach the hook, so
  triggers won't work there.
