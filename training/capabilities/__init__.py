"""Training prompts, one module per capability.

A capability module has `build(seed) -> list[dict]`: prompts that each carry their own answer (see
core.py for the fields). A new tool gets its own module here and a line in CAPABILITIES; nothing
else in the framework needs to change. Never rename or reorder a module's prompts once collected:
collect.py's --skip-done matches turns by their text.
"""

from capabilities import claims, contrasts, core, phrasings, planner

# name -> module. The name is stored on every prompt, so reports can break results down by it.
CAPABILITIES = {
    "core": core,
    "claims": claims,
    "phrasings": phrasings,
    "planner": planner,
    "contrasts": contrasts,
}
